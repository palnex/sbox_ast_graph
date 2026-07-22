"""needle_torch — PyTorch port of the Needle Simple Attention Network.

Public API used by Tasks 2C, 2D, 7, 8:
    from needle_torch import NeedleModel, TransformerConfig
"""

from .config import TransformerConfig
from .model import NeedleModel

__all__ = ["NeedleModel", "TransformerConfig"]
