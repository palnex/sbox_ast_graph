# librarian_ai/parser.py
import os
import json
import urllib.request
import numpy as np
import onnxruntime as ort

try:
    from needle import get_tokenizer
    from needle.model.run import _build_encoder_input
except ImportError:
    raise ImportError("[NeedleParser] Пакет 'needle' не встановлено. Виконайте: pip install git+https://github.com/cactus-compute/needle.git")

class NeedleParser:
    """
    Універсальний локальний лінгвістичний парсер на базі ONNX моделей Needle 26M.
    Автоматично створює потрібні папки та завантажує моделі при першому запуску.
    """
    def __init__(self, model_dir=None):
        if model_dir is None:
            # Дефолтний шлях: librarian_ai/models/needle/
            base_dir = os.path.dirname(os.path.abspath(__file__))
            model_dir = os.path.join(base_dir, "models", "needle")
            
        self.model_dir = model_dir
        self.encoder_path = os.path.join(model_dir, "encoder.onnx")
        self.decoder_path = os.path.join(model_dir, "decoder_step.onnx")
        self.tokenizer_path = os.path.join(model_dir, "tokenizer.model")
        
        # Запускаємо автоматичну підготовку папок та завантаження моделей
        self._bootstrap_models()
                
        # Ініціалізація токенізатора та ONNX Runtime
        self.tokenizer = get_tokenizer(self.tokenizer_path)
        
        sess_opts = ort.SessionOptions()
        sess_opts.intra_op_num_threads = 2
        sess_opts.inter_op_num_threads = 2
        
        self.encoder_sess = ort.InferenceSession(self.encoder_path, sess_opts, providers=['CPUExecutionProvider'])
        self.decoder_sess = ort.InferenceSession(self.decoder_path, sess_opts, providers=['CPUExecutionProvider'])
        
        # Стабільні ID токенів для декодування
        self.bos_id = 1
        self.eos_id = 2
        self.allowed_repeats = [
            self._get_token_id("}"), self._get_token_id("]"), self._get_token_id('"'),
            self._get_token_id(","), self._get_token_id(":"), self._get_token_id("{")
        ]

    def _bootstrap_models(self):
        """
        Перевіряє наявність папки та файлів. 
        Якщо папки немає — створює її. Якщо файлів немає — скачує їх.
        """
        if not os.path.exists(self.model_dir):
            print(f"[NeedleParser] Створення робочої папки для моделей: {self.model_dir}")
            os.makedirs(self.model_dir, exist_ok=True)

        # Перевірка на старий невідповідний токенізатор (122 KB)
        if os.path.exists(self.tokenizer_path):
            if os.path.getsize(self.tokenizer_path) < 124000:
                print("[NeedleParser] Знайдено застарілий токенізатор. Оновлюємо...")
                os.remove(self.tokenizer_path)

        # Джерела для скачування
        sources = {
            "tokenizer.model": "https://huggingface.co/onnx-community/needle-onnx/resolve/main/needle.model",
            "encoder.onnx": "https://huggingface.co/onnx-community/needle-onnx/resolve/main/encoder.onnx",
            "decoder_step.onnx": "https://huggingface.co/onnx-community/needle-onnx/resolve/main/decoder_step.onnx"
        }

        for filename, url in sources.items():
            dest_path = os.path.join(self.model_dir, filename)
            if not os.path.exists(dest_path):
                print(f"\n[NeedleParser] Файл {filename} відсутній локально.")
                print(f"[NeedleParser] Починаємо автоматичне фонове завантаження...")
                
                try:
                    def report_hook(block_num, block_size, total_size):
                        read_so_far = block_num * block_size
                        if total_size > 0:
                            percent = read_so_far * 1e2 / total_size
                            print(f"\r Завантаження {filename}: {percent:.1f}% ({read_so_far // 1024} KB / {total_size // 1024} KB)", end="")
                        else:
                            print(f"\r Завантажено: {read_so_far // 1024} KB", end="")

                    urllib.request.urlretrieve(url, dest_path, reporthook=report_hook)
                    print(f"\n[NeedleParser] [OK] Файл {filename} успішно збережено.")
                except Exception as e:
                    print(f"\n[NeedleParser] [Error] Не вдалося завантажити {filename}: {e}")
                    raise FileNotFoundError(f"Файл {filename} має бути завантажений вручну в {self.model_dir}")

    def _get_token_id(self, char):
        if hasattr(self.tokenizer, "sp") and hasattr(self.tokenizer.sp, "piece_to_id"):
            return self.tokenizer.sp.piece_to_id(char)
        encoded = self.tokenizer.encode(char)
        return encoded[-1] if encoded else 0

    def parse(self, query: str, tools: list, max_new_tokens: int = 96, repetition_penalty: float = 3.5) -> str:
        """
        Виконує синтаксичний та семантичний аналіз запиту.
        """
        tools_str = json.dumps(tools)
        input_ids = _build_encoder_input(self.tokenizer, query, tools_str, max_enc_len=1024)
        input_tensor = np.array([input_ids], dtype=np.int64)

        # 1. Запуск Енкодера
        encoder_outputs = self.encoder_sess.run(None, {self.encoder_sess.get_inputs()[0].name: input_tensor})
        encoder_out = encoder_outputs[0]

        # 2. Цикл Декодера
        past_self_kv = np.zeros((8, 2, 1, 4, 0, 64), dtype=np.float32)
        decoder_input_ids = np.array([[self.bos_id]], dtype=np.int64)

        generated_tokens = []
        penalty_window = 12

        for step in range(max_new_tokens):
            inputs = {
                "decoder_input_ids": decoder_input_ids,
                "encoder_out": encoder_out,
                "past_self_kv": past_self_kv
            }

            outputs = self.decoder_sess.run(None, inputs)
            logits = outputs[0]
            present_self_kv = outputs[1]

            step_logits = logits[0, -1, :]

            # Штраф за повторення токенів
            recent_tokens = set(generated_tokens[-penalty_window:]) if generated_tokens else set()
            for token_id in recent_tokens:
                if token_id not in self.allowed_repeats:
                    if step_logits[token_id] > 0:
                        step_logits[token_id] /= repetition_penalty
                    else:
                        step_logits[token_id] *= repetition_penalty

            next_token = int(np.argmax(step_logits))

            if next_token == self.eos_id:
                break

            generated_tokens.append(next_token)
            decoder_input_ids = np.array([[next_token]], dtype=np.int64)
            past_self_kv = present_self_kv

            # Умова швидкого виходу для JSON
            current_text = self.tokenizer.decode(generated_tokens).strip()
            if current_text.endswith("}}]") or current_text.endswith("}]"):
                break

        return self.tokenizer.decode(generated_tokens)