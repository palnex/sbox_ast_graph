# librarian_ai/test_needle_onnx.py
import os
import json
import urllib.request
import numpy as np
import onnxruntime as ort

try:
    from needle import get_tokenizer
    from needle.model.run import _build_encoder_input
except ImportError:
    print("[Error] Будь ласка, встановіть пакет needle:")
    print("pip install git+https://github.com/cactus-compute/needle.git")
    exit(1)

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
MODEL_DIR = os.path.join(BASE_DIR, "models", "needle")

ENCODER_PATH = os.path.join(MODEL_DIR, "encoder.onnx")
DECODER_PATH = os.path.join(MODEL_DIR, "decoder_step.onnx")
TOKENIZER_PATH = os.path.join(MODEL_DIR, "tokenizer.model")

URL_ENCODER = "https://huggingface.co/onnx-community/needle-onnx/resolve/main/encoder.onnx"
URL_DECODER = "https://huggingface.co/onnx-community/needle-onnx/resolve/main/decoder_step.onnx"
URL_TOKENIZER = "https://huggingface.co/onnx-community/needle-onnx/resolve/main/needle.model"

def ensure_onnx_files():
    """Перевіряє та завантажує файли за потреби."""
    if not os.path.exists(MODEL_DIR):
        os.makedirs(MODEL_DIR, exist_ok=True)
    if os.path.exists(TOKENIZER_PATH) and os.path.getsize(TOKENIZER_PATH) < 124000:
        os.remove(TOKENIZER_PATH)

    def download(url, destination):
        filename = os.path.basename(destination)
        print(f"[Download] Downloading {filename}...")
        urllib.request.urlretrieve(url, destination)

    if not os.path.exists(TOKENIZER_PATH):
        download(URL_TOKENIZER, TOKENIZER_PATH)
    if not os.path.exists(ENCODER_PATH):
        download(URL_ENCODER, ENCODER_PATH)
    if not os.path.exists(DECODER_PATH):
        download(URL_DECODER, DECODER_PATH)

def run_onnx_inference():
    print("=== Needle ONNX Hybrid Test Pipeline ===")
    ensure_onnx_files()

    # 1. Завантаження офіційного токенізатора
    tokenizer = get_tokenizer(TOKENIZER_PATH)

    # 2. Ініціалізація ONNX Runtime сесій
    sess_opts = ort.SessionOptions()
    sess_opts.intra_op_num_threads = 2
    sess_opts.inter_op_num_threads = 2

    encoder_sess = ort.InferenceSession(ENCODER_PATH, sess_opts, providers=['CPUExecutionProvider'])
    decoder_sess = ort.InferenceSession(DECODER_PATH, sess_opts, providers=['CPUExecutionProvider'])

    # 3. Підготовка запиту
    query = "Skip this track and play the next song in the player"
    tools = [
        {
            "name": "media_next_track",
            "description": "Plays the next track in the active media player.",
            "parameters": {}
        }
    ]

    tools_str = json.dumps(tools)
    input_ids = _build_encoder_input(tokenizer, query, tools_str, max_enc_len=1024)
    input_tensor = np.array([input_ids], dtype=np.int64)

    # 4. Запуск Енкодера
    print("\n[3/3] Step 1: Running Encoder...")
    encoder_outputs = encoder_sess.run(None, {encoder_sess.get_inputs()[0].name: input_tensor})
    encoder_out = encoder_outputs[0]

    # 5. Авторегресивний цикл Декодера
    print("Step 2: Running Decoder generation loop...")
    
    bos_id = 1
    eos_id = 2

    past_self_kv = np.zeros((8, 2, 1, 4, 0, 64), dtype=np.float32)
    decoder_input_ids = np.array([[bos_id]], dtype=np.int64)

    generated_tokens = []
    max_new_tokens = 96

    REPETITION_PENALTY = 3.5
    PENALTY_WINDOW = 12

    def get_token_id(char):
        if hasattr(tokenizer, "sp") and hasattr(tokenizer.sp, "piece_to_id"):
            return tokenizer.sp.piece_to_id(char)
        encoded = tokenizer.encode(char)
        return encoded[-1] if encoded else 0

    allowed_repeats = [
        get_token_id("}"),
        get_token_id("]"),
        get_token_id('"'),
        get_token_id(","),
        get_token_id(":"),
        get_token_id("{")
    ]

    for step in range(max_new_tokens):
        inputs = {
            "decoder_input_ids": decoder_input_ids,
            "encoder_out": encoder_out,
            "past_self_kv": past_self_kv
        }

        outputs = decoder_sess.run(None, inputs)
        logits = outputs[0]
        present_self_kv = outputs[1]

        step_logits = logits[0, -1, :]

        # Застосування штрафу за повтори
        recent_tokens = set(generated_tokens[-PENALTY_WINDOW:]) if generated_tokens else set()
        for token_id in recent_tokens:
            if token_id not in allowed_repeats:
                if step_logits[token_id] > 0:
                    step_logits[token_id] /= REPETITION_PENALTY
                else:
                    step_logits[token_id] *= REPETITION_PENALTY

        # Обираємо найкращий токен
        next_token = int(np.argmax(step_logits))

        if next_token == eos_id:
            break

        generated_tokens.append(next_token)
        decoder_input_ids = np.array([[next_token]], dtype=np.int64)
        past_self_kv = present_self_kv

        # --- КЛЮЧОВИЙ КРОК ЗУПИНКИ JSON ---
        # Розкодовуємо накопичений текст на кожному кроці
        current_text = tokenizer.decode(generated_tokens).strip()
        
        # Якщо дужки закрились - миттєво припиняємо генерацію!
        if current_text.endswith("}}]") or current_text.endswith("}]"):
            break

    # Фінальний текст
    result_text = tokenizer.decode(generated_tokens)

    print("\n=== NEEDLE ONNX TOOL ROUTING RESPONSE ===")
    print(result_text)
    print("=========================================")

if __name__ == "__main__":
    run_onnx_inference()