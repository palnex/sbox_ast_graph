# librarian_ai/run_comparison_test_onnx.py
import os
import json
import time
import numpy as np
import onnxruntime as ort

try:
    from needle import get_tokenizer
    from needle.model.run import _build_encoder_input
except ImportError:
    print("[Error] Пакет needle не знайдено. Запустіть скрипт у потрібному оточенні.")
    exit(1)

BASE_DIR = os.path.dirname(os.path.abspath(__file__))

# Шляхи до Базової ONNX моделі
BASE_MODEL_DIR = os.path.join(BASE_DIR, "models", "needle")
BASE_ENCODER = os.path.join(BASE_MODEL_DIR, "encoder.onnx")
BASE_DECODER = os.path.join(BASE_MODEL_DIR, "decoder_step.onnx")
TOKENIZER_PATH = os.path.join(BASE_MODEL_DIR, "tokenizer.model")

# Шляхи до Донавченої ONNX моделі
FT_MODEL_DIR = os.path.join(BASE_DIR, "models", "fine_tunes", "ua_en")
FT_ENCODER = os.path.join(FT_MODEL_DIR, "encoder_ft.onnx")
FT_DECODER = os.path.join(FT_MODEL_DIR, "decoder_step_ft.onnx")

# Схема нашого єдиного інструменту Тета-ролей
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
TOOLS_STR = json.dumps(semantic_tools)

# 6 характерних тестових сценаріїв
TEST_CASES = [
    "If the player jumps, run the JumpSound function using low pitch",
    "When the zombie dies, trigger SpawnLoot with gold_chest",
    "Execute the Explode function with high intensity",
    "If health drops to zero, call GameOver with delay",
    "Run the SaveGame method on local drive",
    "If the user is crouching, reduce movement speed to half"
]

def run_onnx_inference(encoder_sess, decoder_sess, tokenizer, query):
    start_time = time.perf_counter()
    input_ids = _build_encoder_input(tokenizer, query, TOOLS_STR, max_enc_len=1024)
    input_tensor = np.array([input_ids], dtype=np.int64)

    # 1. Енкодер
    encoder_outputs = encoder_sess.run(None, {encoder_sess.get_inputs()[0].name: input_tensor})
    encoder_out = encoder_outputs[0]

    # 2. Декодер
    bos_id = 1
    eos_id = 2
    past_self_kv = np.zeros((8, 2, 1, 4, 0, 64), dtype=np.float32)
    decoder_input_ids = np.array([[bos_id]], dtype=np.int64)

    generated_tokens = []
    max_new_tokens = 96
    repetition_penalty = 3.5
    penalty_window = 12

    def get_token_id(char):
        if hasattr(tokenizer, "sp") and hasattr(tokenizer.sp, "piece_to_id"):
            return tokenizer.sp.piece_to_id(char)
        encoded = tokenizer.encode(char)
        return encoded[-1] if encoded else 0

    allowed_repeats = [
        get_token_id("}"), get_token_id("]"), get_token_id('"'),
        get_token_id(","), get_token_id(":"), get_token_id("{")
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

        # Штраф за повторення токенів
        recent_tokens = set(generated_tokens[-penalty_window:]) if generated_tokens else set()
        for token_id in recent_tokens:
            if token_id not in allowed_repeats:
                if step_logits[token_id] > 0:
                    step_logits[token_id] /= repetition_penalty
                else:
                    step_logits[token_id] *= repetition_penalty

        next_token = int(np.argmax(step_logits))

        if next_token == eos_id:
            break

        generated_tokens.append(next_token)
        decoder_input_ids = np.array([[next_token]], dtype=np.int64)
        past_self_kv = present_self_kv

        current_text = tokenizer.decode(generated_tokens).strip()
        if current_text.endswith("}}]") or current_text.endswith("}]"):
            break

    elapsed_ms = (time.perf_counter() - start_time) * 1000
    result_text = tokenizer.decode(generated_tokens)
    return result_text, elapsed_ms

def main():
    print("==================================================")
    print("=== NEEDLE ONNX COMPARISON BENCHMARK (FAST CPU) ===")
    print("==================================================")

    if not os.path.exists(FT_ENCODER) or not os.path.exists(FT_DECODER):
        print("[Error] Донавчені ONNX файли не знайдено.")
        print("Будь ласка, запустіть спочатку скрипт export_to_onnx.py у WSL2.")
        return

    tokenizer = get_tokenizer(TOKENIZER_PATH)

    sess_opts = ort.SessionOptions()
    sess_opts.intra_op_num_threads = 2
    sess_opts.inter_op_num_threads = 2

    print("\nЗавантаження ONNX сесій у пам'ять Windows...")
    base_enc = ort.InferenceSession(BASE_ENCODER, sess_opts, providers=['CPUExecutionProvider'])
    base_dec = ort.InferenceSession(BASE_DECODER, sess_opts, providers=['CPUExecutionProvider'])
    
    ft_enc = ort.InferenceSession(FT_ENCODER, sess_opts, providers=['CPUExecutionProvider'])
    ft_dec = ort.InferenceSession(FT_DECODER, sess_opts, providers=['CPUExecutionProvider'])
    print("[OK] Всі сесії успішно ініціалізовано!")

    print("\nЗапуск швидкого порівняльного тесту (ONNX CPU)...")
    for idx, query in enumerate(TEST_CASES, 1):
        print(f"\n[Тест {idx}] Запит: \"{query}\"")
        
        # Базовий ONNX запуск
        base_resp, base_time = run_onnx_inference(base_enc, base_dec, tokenizer, query)
        print(f" ├─ BASE ONNX (Затримка: {base_time:.1f}ms):")
        print(f" │    {base_resp}")
        
        # Донавчений ONNX запуск
        ft_resp, ft_time = run_onnx_inference(ft_enc, ft_dec, tokenizer, query)
        print(f" └─ FT ONNX   (Затримка: {ft_time:.1f}ms):")
        print(f"      {ft_resp}")

if __name__ == "__main__":
    main()