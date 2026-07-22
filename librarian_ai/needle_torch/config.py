"""TransformerConfig: mirrors the Flax architecture.py dataclass field-for-field."""

from dataclasses import dataclass, field


@dataclass
class TransformerConfig:
    vocab_size: int = 8192
    d_model: int = 128
    num_heads: int = 4
    num_kv_heads: int = 2
    num_encoder_layers: int = 2
    num_decoder_layers: int = 2
    d_ff: int = 512
    max_seq_len: int = 128
    pad_token_id: int = 0
    rope_theta: float = 10000.0
    dtype: str = "bfloat16"
    activation: str = "drelu"
    num_memory_slots: int = 64
    dropout_rate: float = 0.1
    contrastive_dim: int = 128
    no_feedforward: bool = True
    rope_keys_only: bool = False

    def __init__(self, **kwargs):
        # Set defaults first
        for f_name, f_obj in self.__dataclass_fields__.items():
            setattr(self, f_name, f_obj.default)
        # Override with provided kwargs
        valid = set(self.__dataclass_fields__.keys())
        for k, v in kwargs.items():
            if k in valid:
                setattr(self, k, v)

    @property
    def head_dim(self) -> int:
        return self.d_model // self.num_heads

    @property
    def total_layers(self) -> int:
        return self.num_encoder_layers + self.num_decoder_layers
