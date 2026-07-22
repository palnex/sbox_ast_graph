"""Building-block nn.Modules for the Needle Simple Attention Network.

ZCRMSNorm  — zero-centred RMSNorm: (1+γ)*x / RMS(x)
RoPE       — pre-computed cos/sin freqs + static apply()
MultiHeadAttention — GQA, optional RoPE, optional past-KV caching
"""

import math
import torch
import torch.nn as nn
import torch.nn.functional as F

from .config import TransformerConfig


# ---------------------------------------------------------------------------
# ZCRMSNorm
# ---------------------------------------------------------------------------

class ZCRMSNorm(nn.Module):
    """Zero-centred RMSNorm.

    Formula: (1 + γ) * x / RMS(x)
    where γ is a learnable scale initialized to zero.

    Matches Flax architecture.py ZCRMSNorm exactly.
    """

    def __init__(self, d: int, epsilon: float = 1e-6):
        super().__init__()
        self.epsilon = epsilon
        # γ initialized to zero — param named "scale" to match Flax
        self.scale = nn.Parameter(torch.zeros(d))

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        # Compute RMS in float32 for stability, then cast back
        orig_dtype = x.dtype
        x_f32 = x.float()
        rms = torch.sqrt(x_f32.pow(2).mean(dim=-1, keepdim=True) + self.epsilon)
        return ((1.0 + self.scale) * x_f32 / rms).to(orig_dtype)


# ---------------------------------------------------------------------------
# RoPE
# ---------------------------------------------------------------------------

class RoPE(nn.Module):
    """Pre-computed rotary position embeddings.

    Buffers are NOT parameters (no gradient needed).
    Exposes a static apply() helper for use inside MultiHeadAttention.
    """

    def __init__(self, head_dim: int, max_seq_len: int, theta: float = 10000.0):
        super().__init__()
        # freqs: (head_dim//2,)
        half = head_dim // 2
        freqs = 1.0 / (theta ** (torch.arange(0, head_dim, 2).float() / head_dim))
        t = torch.arange(max_seq_len).float()
        angles = torch.outer(t, freqs)  # (max_seq_len, half)
        self.register_buffer("cos", torch.cos(angles), persistent=False)
        self.register_buffer("sin", torch.sin(angles), persistent=False)

    @staticmethod
    def apply(x: torch.Tensor, cos: torch.Tensor, sin: torch.Tensor) -> torch.Tensor:
        """Apply RoPE to x of shape (B, num_heads, T, head_dim).

        Matches Flax apply_rope():
            x1 = x[..., :half]  x2 = x[..., half:]
            return cat([x1*cos - x2*sin, x2*cos + x1*sin], dim=-1)
        """
        T = x.shape[2]
        # cos/sin are (max_seq_len, half); slice to T and broadcast
        cos_t = cos[:T].unsqueeze(0).unsqueeze(0)  # (1, 1, T, half)
        sin_t = sin[:T].unsqueeze(0).unsqueeze(0)
        half = x.shape[-1] // 2
        x1, x2 = x[..., :half], x[..., half:]
        return torch.cat([x1 * cos_t - x2 * sin_t,
                          x2 * cos_t + x1 * sin_t], dim=-1)

    def get_cos_sin(self, seq_len: int):
        """Return (cos, sin) buffers sliced to seq_len."""
        return self.cos[:seq_len], self.sin[:seq_len]


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def make_causal_mask(seq: int, past_seq: int = 0, device=None) -> torch.Tensor:
    """Lower-triangular bool mask of shape (1, 1, seq, seq+past_seq).

    Position i in the query can attend to positions 0..i+past_seq inclusive.
    """
    total = seq + past_seq
    # rows = current positions (past_seq .. total-1 in the full KV sequence)
    # columns = all KV positions (0 .. total-1)
    row_idx = torch.arange(past_seq, total, device=device).unsqueeze(1)  # (seq, 1)
    col_idx = torch.arange(total, device=device).unsqueeze(0)            # (1, total)
    mask = row_idx >= col_idx  # (seq, total)
    return mask.unsqueeze(0).unsqueeze(0)  # (1, 1, seq, total)


def make_padding_mask(tokens: torch.Tensor, pad_token_id: int) -> torch.Tensor:
    """Boolean padding mask: True where token != pad. Shape (B, 1, 1, T)."""
    return (tokens != pad_token_id).unsqueeze(1).unsqueeze(2)


# ---------------------------------------------------------------------------
# MultiHeadAttention
# ---------------------------------------------------------------------------

class MultiHeadAttention(nn.Module):
    """Grouped-query attention with optional RoPE and past-KV caching.

    Args:
        config: TransformerConfig
        is_cross_attn: if True, Q comes from one source and K/V from another.
        is_causal: if True, applies causal mask (for decoder self-attention).
            Also enables past_kv acceptance in forward().
    """

    def __init__(self, config: TransformerConfig, is_cross_attn: bool = False,
                 is_causal: bool = False):
        super().__init__()
        self.num_heads = config.num_heads
        self.num_kv_heads = config.num_kv_heads
        self.d_model = config.d_model
        self.head_dim = config.d_model // config.num_heads
        self.is_cross_attn = is_cross_attn
        self.is_causal = is_causal
        self.rope_keys_only = config.rope_keys_only
        self.repeats = config.num_heads // config.num_kv_heads

        kv_dim = config.num_kv_heads * self.head_dim

        # Projections — no bias, matching Flax use_bias=False
        self.q_proj = nn.Linear(config.d_model, config.d_model, bias=False)
        self.k_proj = nn.Linear(config.d_model, kv_dim, bias=False)
        self.v_proj = nn.Linear(config.d_model, kv_dim, bias=False)
        self.out_proj = nn.Linear(config.d_model, config.d_model, bias=False)

        # Per-head QK norms (applied after reshape, before GQA expand)
        # q_norm operates on num_heads heads of head_dim
        # k_norm operates on num_kv_heads heads of head_dim
        self.q_norm = ZCRMSNorm(self.head_dim)
        self.k_norm = ZCRMSNorm(self.head_dim)

        self._scale = math.sqrt(self.head_dim)

    def forward(
        self,
        q_input: torch.Tensor,
        kv_input: torch.Tensor,
        mask: torch.Tensor | None = None,
        rope: tuple[torch.Tensor, torch.Tensor] | None = None,
        past_kv: tuple[torch.Tensor, torch.Tensor] | None = None,
    ):
        """
        Args:
            q_input:  (B, T_q, d_model)
            kv_input: (B, T_kv, d_model)
            mask:     (B, 1, T_q, T_kv) bool — True = attend
            rope:     (cos, sin) tensors of shape (T, head_dim//2) each
            past_kv:  (k_cache, v_cache), each (B, num_kv_heads, past_T, head_dim)
                      Only used when is_causal=True (decoder self-attn).

        Returns:
            out:        (B, T_q, d_model)
            present_kv: (k, v) each (B, num_kv_heads, T_total, head_dim)
        """
        B, T_q, _ = q_input.shape

        q = self.q_proj(q_input)   # (B, T_q, d_model)
        k = self.k_proj(kv_input)  # (B, T_kv, kv_dim)
        v = self.v_proj(kv_input)  # (B, T_kv, kv_dim)

        # Reshape to (B, num_heads/num_kv_heads, T, head_dim)
        q = q.reshape(B, T_q, self.num_heads, self.head_dim).transpose(1, 2)
        T_kv = k.shape[1]
        k = k.reshape(B, T_kv, self.num_kv_heads, self.head_dim).transpose(1, 2)
        v = v.reshape(B, T_kv, self.num_kv_heads, self.head_dim).transpose(1, 2)

        # QK norms (on the head_dim axis, before GQA expansion)
        q = self.q_norm(q)
        k = self.k_norm(k)

        # RoPE application — applied to the CURRENT q and k only.
        # Cached entries in past_kv already have RoPE at their original positions
        # baked in; re-applying after cache-concat would double-rotate them.
        if rope is not None:
            cos, sin = rope
            if not self.rope_keys_only:
                q = RoPE.apply(q, cos, sin)
            k = RoPE.apply(k, cos, sin)

        # Concatenate past KV (decoder self-attn only).
        # past_kv stores K with its original-position RoPE already applied.
        if past_kv is not None:
            k_past, v_past = past_kv
            k = torch.cat([k_past, k], dim=2)   # (B, num_kv_heads, past_T+T_kv, head_dim)
            v = torch.cat([v_past, v], dim=2)

        present_kv = (k, v)

        # GQA expansion: repeat K and V to match num_heads
        if self.repeats > 1:
            k = k.repeat_interleave(self.repeats, dim=1)  # (B, num_heads, T_total, head_dim)
            v = v.repeat_interleave(self.repeats, dim=1)

        # Scaled dot-product attention
        # q: (B, num_heads, T_q, head_dim)
        # k: (B, num_heads, T_total, head_dim)
        attn_weights = torch.matmul(q, k.transpose(-2, -1)) / self._scale  # (B, H, T_q, T_total)

        if mask is not None:
            # mask: True = attend, False = block
            # Fill blocked positions with -inf
            attn_weights = attn_weights.masked_fill(~mask, float("-inf"))

        attn_weights = F.softmax(attn_weights.float(), dim=-1).to(q.dtype)

        out = torch.matmul(attn_weights, v)           # (B, num_heads, T_q, head_dim)
        out = out.transpose(1, 2).reshape(B, T_q, self.d_model)
        out = self.out_proj(out)

        return out, present_kv
