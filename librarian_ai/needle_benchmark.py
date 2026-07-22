# librarian_ai/needle_benchmark.py
import os
import json
import time
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

def run_benchmark():
    print("==================================================")
    print("=== NEEDLE ONNX LOCAL CPU BENCHMARK RUNNER ===")
    print("==================================================")
    
    ensure_onnx_files()

    tokenizer = get_tokenizer(TOKENIZER_PATH)

    sess_opts = ort.SessionOptions()
    sess_opts.intra_op_num_threads = 2
    sess_opts.inter_op_num_threads = 2

    print("\n[Loading] Loading ONNX models into memory...")
    encoder_sess = ort.InferenceSession(ENCODER_PATH, sess_opts, providers=['CPUExecutionProvider'])
    decoder_sess = ort.InferenceSession(DECODER_PATH, sess_opts, providers=['CPUExecutionProvider'])
    print("[Loading] Models loaded successfully!\n")

    # --- ВИЗНАЧЕННЯ НАБОРУ БЕНЧМАРК-ТЕСТІВ ---
    
    # 1. Схема стандартних медіа-інструментів
    media_tools = [
        {
            "name": "media_next_track",
            "description": "Plays the next track in the active media player.",
            "parameters": {}
        },
        {
            "name": "media_set_volume",
            "description": "Sets the volume of the media player.",
            "parameters": {
                "level": {"type": "integer", "description": "Volume level from 0 to 100"}
            }
        }
    ]

    # 2. Схема нашого майбутнього цільового інструменту (Тета-ролі)
    semantic_tools = [
        {
            "name": "extract_semantic_roles",
            "description": "Extracts syntactic linguistic roles from the user query.",
            "parameters": {
                "agent": {"type": "string", "description": "Who or what performs the action"},
                "action": {"type": "string", "description": "The action, verb, or operation being performed"},
                "patient": {"type": "string", "description": "The target, class, or entity affected by the action"},
                "instrument": {"type": "string", "description": "The tool, method, or helper used"},
                "condition": {"type": "string", "description": "Triggers, conditions, or if-clauses"}
            }
        }
    ]

    test_cases = [
        {
            "id": "Test 1 [Good/Standard]",
            "query": "Skip this song, please",
            "tools": media_tools
        },
        {
            "id": "Test 2 [Good/Standard Volume]",
            "query": "Make the player volume 85 percent right now",
            "tools": media_tools
        },
        {
            "id": "Test 3 [Gibberish/No Match Noise]",
            "query": "I ate a green apple yesterday with my friends in the office",
            "tools": media_tools
        },
        {
            "id": "Test 4 [Custom Semantic Roles - Zero Shot]",
            "query": "If the player jumps, run the JumpSound function using low pitch",
            "tools": semantic_tools
        }
    ]

    # Допоміжна функція отримання токенів
    def get_token_id(char):
        if hasattr(tokenizer, "sp") and hasattr(tokenizer.sp, "piece_to_id"):
            return tokenizer.sp.piece_to_id(char)
        encoded = tokenizer.encode(char)
        return encoded[-1] if encoded else 0

    allowed_repeats = [
        get_token_id("}"), get_token_id("]"), get_token_id('"'),
        get_token_id(","), get_token_id(":"), get_token_id("{")
    ]

    # --- ЗАПУСК ЦИКЛУ ОЦІНКИ ---
    for case in test_cases:
        print("--------------------------------------------------")
        print(f" {case['id']}")
        print(f" Query: \"{case['query']}\"")
        print("--------------------------------------------------")

        # Фіксуємо початковий час для всього кроку
        start_time = time.perf_counter()

        # Пакування промпту
        tools_str = json.dumps(case["tools"])
        input_ids = _build_encoder_input(tokenizer, case["query"], tools_str, max_enc_len=1024)
        input_tensor = np.array([input_ids], dtype=np.int64)

        # 1. Запуск Енкодера
        encoder_outputs = encoder_sess.run(None, {encoder_sess.get_inputs()[0].name: input_tensor})
        encoder_out = encoder_outputs[0]

        # 2. Цикл Декодера
        bos_id = 1
        eos_id = 2

        past_self_kv = np.zeros((8, 2, 1, 4, 0, 64), dtype=np.float32)
        decoder_input_ids = np.array([[bos_id]], dtype=np.int64)

        generated_tokens = []
        max_new_tokens = 96
        REPETITION_PENALTY = 3.5
        PENALTY_WINDOW = 12

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

            # Штраф за повтори
            recent_tokens = set(generated_tokens[-PENALTY_WINDOW:]) if generated_tokens else set()
            for token_id in recent_tokens:
                if token_id not in allowed_repeats:
                    if step_logits[token_id] > 0:
                        step_logits[token_id] /= REPETITION_PENALTY
                    else:
                        step_logits[token_id] *= REPETITION_PENALTY

            next_token = int(np.argmax(step_logits))

            if next_token == eos_id:
                break

            generated_tokens.append(next_token)
            decoder_input_ids = np.array([[next_token]], dtype=np.int64)
            past_self_kv = present_self_kv

            # Умова раннього завершення для JSON
            current_text = tokenizer.decode(generated_tokens).strip()
            if current_text.endswith("}}]") or current_text.endswith("}]"):
                break

        # Фіксуємо кінцевий час
        elapsed_time_ms = (time.perf_counter() - start_time) * 1000
        result_text = tokenizer.decode(generated_tokens)

        # Вивід метрик
        print(f" Latency: {elapsed_time_ms:.2f} ms")
        print(f" Tokens generated: {len(generated_tokens)}")
        print(f" Response: {result_text}")
        print("--------------------------------------------------\n")

    print("=== БЕНЧМАРК ЗАКІНЧЕНО ===")

if __name__ == "__main__":
    run_benchmark()