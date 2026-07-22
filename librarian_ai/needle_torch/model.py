"""Needle Simple Attention Network — PyTorch port.

Encoder, Decoder, NeedleModel — parametric on TransformerConfig.

Key design decisions:
  - No FFN (no_feedforward=True is the production default; we never implement it).
  - ZCRMSNorm, GQA, RoPE all match architecture.py line-for-line.
  - Decoder.step() is ONNX-traceable: no data-dependent control flow.
  - Tied embedding: decoder logits = hidden @ embedding.weight.T
"""

import math
import torch
import torch.nn as nn
import torch.nn.functional as F

from .config import TransformerConfig
from .layers import ZCRMSNorm, RoPE, MultiHeadAttention, make_causal_mask


# ---------------------------------------------------------------------------
# EncoderBlock
# ---------------------------------------------------------------------------

class EncoderBlock(nn.Module):
    """Pre-norm self-attention with sigmoid-gated residual.

    Matches Flax EncoderBlock.__call__:
        gate = sigmoid(attn_gate)
        x = ZCRMSNorm(x)
        x = self_attn(x, x, ...)
        x = residual + gate * attn_out
    """

    def __init__(self, config: TransformerConfig):
        super().__init__()
        # Scalar gate initialized to zero — sigmoid(0) = 0.5
        self.attn_gate = nn.Parameter(torch.zeros(()))
        self.norm = ZCRMSNorm(config.d_model)
        self.self_attn = MultiHeadAttention(config, is_cross_attn=False, is_causal=False)

    def forward(self, x: torch.Tensor, mask=None, rope=None):
        """
        x:    (B, T, d_model)
        mask: (B, 1, T, T) bool
        rope: (cos, sin) from RoPE buffers
        """
        gate = torch.sigmoid(self.attn_gate)
        residual = x
        x = self.norm(x)
        attn_out, _ = self.self_attn(x, x, mask=mask, rope=rope)
        x = residual + gate * attn_out
        return x


# ---------------------------------------------------------------------------
# DecoderBlock
# ---------------------------------------------------------------------------

class DecoderBlock(nn.Module):
    """Causal self-attn + cross-attn with independent sigmoid-gated residuals.

    Matches Flax DecoderBlock.__call__:
        self_gate  = sigmoid(self_attn_gate)
        x = ZCRMSNorm(x) -> self_attn(x, x) -> x = residual + self_gate * out

        cross_gate = sigmoid(cross_attn_gate)
        x = ZCRMSNorm(x) -> cross_attn(x, encoder_out) -> x = residual + cross_gate * out
    """

    def __init__(self, config: TransformerConfig):
        super().__init__()
        self.self_attn_gate = nn.Parameter(torch.zeros(()))
        self.cross_attn_gate = nn.Parameter(torch.zeros(()))

        # ZCRMSNorm_0 = pre-norm for self-attn
        # ZCRMSNorm_1 = pre-norm for cross-attn
        self.self_norm = ZCRMSNorm(config.d_model)
        self.cross_norm = ZCRMSNorm(config.d_model)

        self.self_attn = MultiHeadAttention(config, is_cross_attn=False, is_causal=True)
        self.cross_attn = MultiHeadAttention(config, is_cross_attn=True, is_causal=False)

    def forward(
        self,
        x: torch.Tensor,
        encoder_out: torch.Tensor,
        self_mask=None,
        cross_mask=None,
        rope=None,
        past_self_kv=None,
    ):
        """
        Args:
            x:            (B, T_dec, d_model)
            encoder_out:  (B, T_enc, d_model)
            self_mask:    (B, 1, T_dec, T_total) bool
            cross_mask:   (B, 1, T_dec, T_enc) bool
            rope:         (cos, sin) for self-attention RoPE
            past_self_kv: (k, v) each (B, num_kv_heads, past_T, head_dim)

        Returns:
            x:            (B, T_dec, d_model)
            present_self_kv: (k, v) each (B, num_kv_heads, T_total, head_dim)
        """
        # --- Causal self-attention ---
        self_gate = torch.sigmoid(self.self_attn_gate)
        residual = x
        x = self.self_norm(x)
        self_out, present_self_kv = self.self_attn(
            x, x, mask=self_mask, rope=rope, past_kv=past_self_kv
        )
        x = residual + self_gate * self_out

        # --- Cross-attention ---
        cross_gate = torch.sigmoid(self.cross_attn_gate)
        residual = x
        x = self.cross_norm(x)
        cross_out, _ = self.cross_attn(x, encoder_out, mask=cross_mask)
        x = residual + cross_gate * cross_out

        return x, present_self_kv


# ---------------------------------------------------------------------------
# Encoder
# ---------------------------------------------------------------------------

class Encoder(nn.Module):
    """Embedding lookup + N EncoderBlocks + final ZCRMSNorm.

    Returns encoder hidden states: (B, T_enc, d_model).
    Note: embedding is shared with Decoder and set externally via .embedding.
    """

    def __init__(self, config: TransformerConfig):
        super().__init__()
        self.config = config
        # Embedding is shared; the NeedleModel assigns it after construction.
        self.embedding: nn.Embedding | None = None
        self.embed_scale = math.sqrt(config.d_model)

        self.layers = nn.ModuleList([
            EncoderBlock(config) for _ in range(config.num_encoder_layers)
        ])
        self.final_norm = ZCRMSNorm(config.d_model)

        head_dim = config.d_model // config.num_heads
        self.rope = RoPE(head_dim, config.max_seq_len, config.rope_theta)

    def forward(self, input_ids: torch.Tensor, mask=None) -> torch.Tensor:
        """
        input_ids: (B, T_enc) long
        mask:      (B, 1, 1, T_enc) bool padding mask (optional)

        Returns: (B, T_enc, d_model)
        """
        assert self.embedding is not None, "Encoder.embedding must be set by NeedleModel"
        x = self.embedding(input_ids) * self.embed_scale

        T = input_ids.shape[1]
        cos, sin = self.rope.get_cos_sin(T)
        rope = (cos, sin)

        for layer in self.layers:
            x = layer(x, mask=mask, rope=rope)

        x = self.final_norm(x)
        return x


# ---------------------------------------------------------------------------
# Decoder
# ---------------------------------------------------------------------------

class Decoder(nn.Module):
    """Embedding lookup + N DecoderBlocks + final ZCRMSNorm + LM head.

    The LM head is a tied projection: logits = hidden @ embedding.weight.T
    The embedding weight is shared with the Encoder/NeedleModel.
    """

    def __init__(self, config: TransformerConfig):
        super().__init__()
        self.config = config
        # Embedding is shared; set by NeedleModel after construction.
        self.embedding: nn.Embedding | None = None
        self.embed_scale = math.sqrt(config.d_model)

        self.layers = nn.ModuleList([
            DecoderBlock(config) for _ in range(config.num_decoder_layers)
        ])
        # ZCRMSNorm_0 in the decoder (final norm after all layers)
        self.final_norm = ZCRMSNorm(config.d_model)

        head_dim = config.d_model // config.num_heads
        self.rope = RoPE(head_dim, config.max_seq_len, config.rope_theta)

    def forward(
        self,
        input_ids: torch.Tensor,
        encoder_out: torch.Tensor,
        self_mask=None,
        cross_mask=None,
    ) -> torch.Tensor:
        """Full-sequence decode (training / teacher-forcing).

        Args:
            input_ids:   (B, T_dec) long
            encoder_out: (B, T_enc, d_model)
            self_mask:   (B, 1, T_dec, T_dec) bool causal mask
            cross_mask:  (B, 1, T_dec, T_enc) bool

        Returns:
            logits: (B, T_dec, vocab_size)
        """
        assert self.embedding is not None
        x = self.embedding(input_ids) * self.embed_scale

        T = input_ids.shape[1]
        cos, sin = self.rope.get_cos_sin(T)
        rope = (cos, sin)

        for layer in self.layers:
            x, _ = layer(x, encoder_out, self_mask=self_mask, cross_mask=cross_mask,
                         rope=rope, past_self_kv=None)

        x = self.final_norm(x)
        # Tied output projection: (B, T, d_model) @ (d_model, vocab_size)
        logits = x.float() @ self.embedding.weight.T
        return logits

    # ------------------------------------------------------------------
    # Autoregressive step — the entry point for ONNX export (Task 7)
    # ------------------------------------------------------------------

    def initial_past_kv(self, batch: int = 1) -> torch.Tensor:
        """Return a zero past_kv tensor for the first step.

        Shape: (num_decoder_layers, 2, batch, num_kv_heads, 0, head_dim)

        Using length-0 in the sequence dimension so the first step's cat
        produces just the current step's KV.
        """
        cfg = self.config
        head_dim = cfg.d_model // cfg.num_heads
        return torch.zeros(
            cfg.num_decoder_layers, 2, batch, cfg.num_kv_heads, 0, head_dim,
            dtype=torch.float32,
        )

    def step(
        self,
        decoder_input_ids: torch.Tensor,
        encoder_kv: torch.Tensor,
        past_self_kv: torch.Tensor,
    ):
        """Single autoregressive decoder step.

        Accepts explicit past KV cache and returns updated KV (present).
        This signature is what torch.onnx.export traces in Task 7.

        Args:
            decoder_input_ids: (B, 1) long — single token per step
            encoder_kv:        (B, T_enc, d_model) — frozen encoder output
            past_self_kv:      (num_decoder_layers, 2, B, num_kv_heads, past_T, head_dim)
                               Use initial_past_kv() for the first step.

        Returns:
            logits:      (B, 1, vocab_size)
            present_kv:  (num_decoder_layers, 2, B, num_kv_heads, past_T+1, head_dim)

        NOTE: No Python control flow that depends on tensor *values* — only
        shape-derived constants — so this is safely ONNX-traceable.
        """
        assert self.embedding is not None
        B = decoder_input_ids.shape[0]

        x = self.embedding(decoder_input_ids) * self.embed_scale  # (B, 1, d_model)

        # RoPE for this one position: offset by past_T
        past_T = past_self_kv.shape[4]
        # We use position (past_T) for the current token.
        # Slice cos/sin at that single position: (1, head_dim//2)
        cos_full, sin_full = self.rope.get_cos_sin(past_T + 1)
        cos = cos_full[past_T:past_T + 1]   # (1, head_dim//2)
        sin = sin_full[past_T:past_T + 1]
        rope = (cos, sin)

        # Causal mask: shape (1, 1, 1, past_T+1) — current token attends all past+self
        self_mask = make_causal_mask(1, past_T, device=x.device)  # (1,1,1, past_T+1)

        present_layers = []
        for i, layer in enumerate(self.layers):
            # Unpack this layer's past KV: each (B, num_kv_heads, past_T, head_dim)
            layer_past_k = past_self_kv[i, 0]  # (B, num_kv_heads, past_T, head_dim)
            layer_past_v = past_self_kv[i, 1]
            layer_past = (layer_past_k, layer_past_v)

            x, (k_new, v_new) = layer(
                x, encoder_kv,
                self_mask=self_mask,
                cross_mask=None,
                rope=rope,
                past_self_kv=layer_past,
            )
            # k_new, v_new: (B, num_kv_heads, past_T+1, head_dim)
            present_layers.append(torch.stack([k_new, v_new], dim=0))  # (2, B, nkv, T+1, hd)

        # Stack layers: (num_decoder_layers, 2, B, num_kv_heads, past_T+1, head_dim)
        present_kv = torch.stack(present_layers, dim=0)

        x = self.final_norm(x)
        logits = x.float() @ self.embedding.weight.T  # (B, 1, vocab_size)
        return logits, present_kv


# ---------------------------------------------------------------------------
# NeedleModel
# ---------------------------------------------------------------------------

class NeedleModel(nn.Module):
    """Top-level Needle Simple Attention Network — PyTorch port.

    Mirrors SimpleAttentionNetwork (Flax).

    Parameters
    ----------
    config : TransformerConfig
        Architecture hyperparameters.  Pass production dims to get the 26M model.
    """

    def __init__(self, config: TransformerConfig):
        super().__init__()
        self.config = config

        # Shared embedding (tied output projection in decoder)
        self.embedding = nn.Embedding(config.vocab_size, config.d_model)
        nn.init.normal_(self.embedding.weight, std=0.02)

        self.encoder = Encoder(config)
        self.decoder = Decoder(config)

        # Wire up shared embedding
        self.encoder.embedding = self.embedding
        self.decoder.embedding = self.embedding

        # Contrastive head — present in the Flax param tree
        # contrastive_hidden: (d_model, d_model//4) with bias
        self.contrastive_hidden = nn.Linear(config.d_model, config.d_model // 4, bias=True)
        # contrastive_proj: (d_model//4, contrastive_dim) no bias
        self.contrastive_proj = nn.Linear(config.d_model // 4, config.contrastive_dim, bias=False)

        # Scalar contrastive temperature
        self.log_temp = nn.Parameter(torch.zeros(()))

    def forward(
        self,
        src: torch.Tensor,
        tgt: torch.Tensor,
        src_mask=None,
        tgt_mask=None,
        cross_mask=None,
    ) -> torch.Tensor:
        """Full encoder-decoder forward pass (training).

        Returns logits: (B, T_dec, vocab_size)
        """
        encoder_out = self.encoder(src, mask=src_mask)
        logits = self.decoder(tgt, encoder_out, self_mask=tgt_mask, cross_mask=cross_mask)
        return logits
