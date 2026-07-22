# librarian_ai/export_to_onnx.py
import os
import sys
import pickle
import argparse
from pathlib import Path

import numpy as np
import torch

# Дозволяємо імпорт папки needle_torch з поточної директорії
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, BASE_DIR)
from needle_torch import NeedleModel, TransformerConfig

# --- Допоміжні функції конвертації ваг ---
def _to_f32(arr):
    return np.asarray(arr).astype(np.float32)

def copy_kernel(new_state, flax_t, pt_name, i=None):
    arr = _to_f32(flax_t)
    if i is not None:
        arr = arr[i]
    arr = arr.T
    new_state[pt_name] = torch.from_numpy(arr.copy())

def copy_vector(new_state, flax_t, pt_name, i=None):
    arr = _to_f32(flax_t)
    if i is not None:
        arr = arr[i]
    new_state[pt_name] = torch.from_numpy(np.array(arr).copy())

class DecoderStepWrapper(torch.nn.Module):
    """Обгортка для кроку декодера для коректного трасування в ONNX"""
    def __init__(self, decoder):
        super().__init__()
        self.decoder = decoder

    def forward(self, decoder_input_ids, encoder_out, past_self_kv):
        return self.decoder.step(decoder_input_ids, encoder_out, past_self_kv)

def export_pkl_to_onnx(pkl_path, encoder_out_path, decoder_out_path):
    print(f"Loading Flax checkpoint from {pkl_path}...", flush=True)
    with open(pkl_path, "rb") as f:
        data = pickle.load(f)
        
    config_dict = data["config"]
    flax_params = data["params"]
    
    # Створюємо модель PyTorch з конфігурацією вашої моделі
    pt_config = TransformerConfig(**config_dict)
    model = NeedleModel(pt_config)
    model.eval()
    
    new_state = {}
    
    # Переносимо ваги з JAX (Flax) в PyTorch в оперативній пам'яті
    copy_vector(new_state, flax_params["log_temp"], "log_temp")

    emb_tensor = torch.from_numpy(_to_f32(flax_params["embedding"]["embedding"]).copy())
    new_state["embedding.weight"] = emb_tensor
    new_state["encoder.embedding.weight"] = emb_tensor
    new_state["decoder.embedding.weight"] = emb_tensor

    copy_kernel(new_state, flax_params["contrastive_hidden"]["kernel"], "contrastive_hidden.weight")
    copy_vector(new_state, flax_params["contrastive_hidden"]["bias"], "contrastive_hidden.bias")
    copy_kernel(new_state, flax_params["contrastive_proj"]["kernel"], "contrastive_proj.weight")

    copy_vector(new_state, flax_params["encoder"]["final_norm"]["scale"], "encoder.final_norm.scale")

    enc_block = flax_params["encoder"]["layers"]["EncoderBlock_0"]
    for i in range(pt_config.num_encoder_layers):
        base = f"encoder.layers.{i}"
        copy_vector(new_state, enc_block["attn_gate"], f"{base}.attn_gate", i)
        copy_vector(new_state, enc_block["ZCRMSNorm_0"]["scale"], f"{base}.norm.scale", i)
        
        sa = enc_block["self_attn"]
        for proj in ["q_proj", "k_proj", "v_proj", "out_proj"]:
            copy_kernel(new_state, sa[proj]["kernel"], f"{base}.self_attn.{proj}.weight", i)
        for n in ["q_norm", "k_norm"]:
            copy_vector(new_state, sa[n]["scale"], f"{base}.self_attn.{n}.scale", i)

    copy_vector(new_state, flax_params["decoder"]["ZCRMSNorm_0"]["scale"], "decoder.final_norm.scale")

    dec_block = flax_params["decoder"]["layers"]["DecoderBlock_0"]
    for i in range(pt_config.num_decoder_layers):
        base = f"decoder.layers.{i}"
        copy_vector(new_state, dec_block["self_attn_gate"], f"{base}.self_attn_gate", i)
        copy_vector(new_state, dec_block["cross_attn_gate"], f"{base}.cross_attn_gate", i)
        copy_vector(new_state, dec_block["ZCRMSNorm_0"]["scale"], f"{base}.self_norm.scale", i)
        copy_vector(new_state, dec_block["ZCRMSNorm_1"]["scale"], f"{base}.cross_norm.scale", i)

        sa = dec_block["self_attn"]
        for proj in ["q_proj", "k_proj", "v_proj", "out_proj"]:
            copy_kernel(new_state, sa[proj]["kernel"], f"{base}.self_attn.{proj}.weight", i)
        for n in ["q_norm", "k_norm"]:
            copy_vector(new_state, sa[n]["scale"], f"{base}.self_attn.{n}.scale", i)

        ca = dec_block["cross_attn"]
        for proj in ["q_proj", "k_proj", "v_proj", "out_proj"]:
            copy_kernel(new_state, ca[proj]["kernel"], f"{base}.cross_attn.{proj}.weight", i)
        for n in ["q_norm", "k_norm"]:
            copy_vector(new_state, ca[n]["scale"], f"{base}.cross_attn.{n}.scale", i)

    model.load_state_dict(new_state, strict=True)
    print("[OK] JAX weights successfully mapped to PyTorch port.", flush=True)

    # Експортуємо Енкодер
    print("Exporting Encoder to ONNX...", flush=True)
    encoder = model.encoder
    dummy_ids = torch.zeros(1, 16, dtype=torch.long)
    torch.onnx.export(
        encoder, (dummy_ids,), encoder_out_path,
        input_names=["input_ids"], output_names=["encoder_out"],
        dynamic_axes={"input_ids": {0: "batch", 1: "seq"},
                      "encoder_out": {0: "batch", 1: "seq"}},
        opset_version=17, do_constant_folding=True, external_data=False, dynamo=False,
    )
    print(f"[OK] Encoder written ({os.path.getsize(encoder_out_path) / 1e6:.1f} MB)", flush=True)

    # Експортуємо Декодер
    print("Exporting Decoder step to ONNX...", flush=True)
    wrapper = DecoderStepWrapper(model.decoder)
    wrapper.eval()
    
    head_dim = pt_config.d_model // pt_config.num_heads
    batch, enc_seq, past_seq = 1, 16, 4
    dummy_dec_ids = torch.zeros(batch, 1, dtype=torch.long)
    dummy_enc_out = torch.zeros(batch, enc_seq, pt_config.d_model, dtype=torch.float32)
    dummy_past_kv = torch.zeros(
        pt_config.num_decoder_layers, 2, batch, pt_config.num_kv_heads, past_seq, head_dim,
        dtype=torch.float32,
    )
    
    torch.onnx.export(
        wrapper, (dummy_dec_ids, dummy_enc_out, dummy_past_kv), decoder_out_path,
        input_names=["decoder_input_ids", "encoder_out", "past_self_kv"],
        output_names=["logits", "present_self_kv"],
        dynamic_axes={
            "decoder_input_ids": {0: "batch"},
            "encoder_out":       {0: "batch", 1: "enc_seq"},
            "past_self_kv":      {2: "batch", 4: "past_seq"},
            "logits":            {0: "batch"},
            "present_self_kv":   {2: "batch", 4: "present_seq"},
        },
        opset_version=17, do_constant_folding=True, external_data=False, dynamo=False,
    )
    print(f"[OK] Decoder step written ({os.path.getsize(decoder_out_path) / 1e6:.1f} MB)", flush=True)

if __name__ == "__main__":
    p = argparse.ArgumentParser(description="Convert and export .pkl checkpoint directly to ONNX.")
    p.add_argument("--pkl", required=True, help="Path to your JAX .pkl model weights")
    p.add_argument("--encoder-out", required=True, help="Output path for encoder.onnx")
    p.add_argument("--decoder-out", required=True, help="Output path for decoder_step.onnx")
    args = p.parse_args()

    export_pkl_to_onnx(args.pkl, args.encoder_out, args.decoder_out)