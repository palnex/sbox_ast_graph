# librarian_ai/train.py
import os
import sys
import time
import shutil
import argparse

# Вимикаємо агресивне резервування 90% відеопам'яті JAX:
os.environ["XLA_PYTHON_CLIENT_PREALLOCATE"] = "false"

try:
    from needle import load_checkpoint
except ImportError:
    print("[Error] Пакет needle або jax не встановлено.")
    exit(1)

BASE_DIR = os.path.dirname(os.path.abspath(__file__))

# Базова модель, яка завжди використовується як фундамент
BASE_MODEL_PKL = os.path.join(BASE_DIR, "models", "needle", "needle.pkl")

# Тимчасова локальна Linux-папка (ext4)
LINUX_TEMP_DIR = "/tmp/needle_fine_tunes"

def run_training(task_name="ua_en", skip_onnx=False):
    print("==================================================")
    print(f"=== STARTING LOCAL NEEDLE FINE-TUNING FOR TASK: {task_name.upper()} ===")
    print("==================================================")
    
    # Динамічні шляхи на основі обраного таска (ua_en, jp_en, neuron тощо)
    train_data_path = os.path.join(BASE_DIR, "training_data", task_name, "train_data.jsonl")
    output_dir = os.path.join(BASE_DIR, "models", "fine_tunes", task_name)
    output_pkl = os.path.join(output_dir, "needle_ft.pkl")

    if not os.path.exists(BASE_MODEL_PKL):
        print(f"[Error] Не знайдено базову модель needle.pkl за шляхом: {BASE_MODEL_PKL}")
        return
        
    if not os.path.exists(train_data_path):
        print(f"[Error] Не знайдено навчальний датасет за шляхом: {train_data_path}")
        print(f"[Info] Будь ласка, створіть папку та покладіть туди файл: librarian_ai/training_data/{task_name}/train_data.jsonl")
        return

    # Створюємо вихідну папку на Windows (вона створиться автоматично для будь-якого таска)
    os.makedirs(output_dir, exist_ok=True)
    
    # Очищаємо та створюємо тимчасову Linux папку
    if os.path.exists(LINUX_TEMP_DIR):
        try:
            shutil.rmtree(LINUX_TEMP_DIR)
        except Exception:
            pass
    os.makedirs(LINUX_TEMP_DIR, exist_ok=True)

    # 1. Перевірка базової моделі
    print("\n[1/3] Перевірка базових ваг та конфігурації...")
    try:
        _, config = load_checkpoint(BASE_MODEL_PKL)
        print(f"[OK] Базову модель з d_model={config.d_model} успішно перевірено.")
    except Exception as e:
        print(f"[Error] Не вдалося зчитати базовий чекпоінт: {e}")
        return

    # 2. Запуск локального навчання в Linux-папці
    print(f"\n[2/3] Запуск навчання у тимчасовій Linux-папці (ext4)...")
    print("--------------------------------------------------")
    
    cmd = (
        f'needle finetune '
        f'--checkpoint "{BASE_MODEL_PKL}" '
        f'--checkpoint-dir "{LINUX_TEMP_DIR}" '
        f'--epochs 15 '
        f'--batch-size 16 '
        f'"{train_data_path}"'
    )
    
    print(f"Executing: {cmd}")
    start_time = time.perf_counter()
    
    exit_code = os.system(cmd)
    
    elapsed = time.perf_counter() - start_time
    print("--------------------------------------------------")

    if exit_code == 0:
        print(f"\n[3/3] [OK] Процес навчання завершено за {elapsed:.2f} сек.")
        
        # Шукаємо згенеровані pkl файли у тимчасовій Linux-папці
        pkl_files = [f for f in os.listdir(LINUX_TEMP_DIR) if f.endswith(".pkl")]
        if pkl_files:
            pkl_files.sort(key=lambda x: os.path.getmtime(os.path.join(LINUX_TEMP_DIR, x)), reverse=True)
            newest_pkl = os.path.join(LINUX_TEMP_DIR, pkl_files[0])
            
            print(f"[OK] Знайдено свіжий чекпоінт у Linux: {newest_pkl}")
            
            # --- ЕТАП ONNX ЕКСПОРТУ ---
            onnx_success = False
            temp_encoder_onnx = os.path.join(LINUX_TEMP_DIR, "encoder_ft.onnx")
            temp_decoder_onnx = os.path.join(LINUX_TEMP_DIR, "decoder_step_ft.onnx")
            
            if not skip_onnx:
                print("\n[ONNX Export] Запуск автоматичного експорту моделі в ONNX формат...")
                try:
                    sys.path.insert(0, BASE_DIR)
                    from export_to_onnx import export_pkl_to_onnx
                    
                    export_pkl_to_onnx(newest_pkl, temp_encoder_onnx, temp_decoder_onnx)
                    onnx_success = True
                    print("[OK] Модель успішно конвертовано в ONNX графіки.")
                except Exception as e:
                    print(f"[Warning] Не вдалося автоматично експортувати модель в ONNX: {e}")
            else:
                print("\n[ONNX Export] Автоматичний експорт ONNX пропущено.")

            # --- БЕЗПЕЧНЕ КОПІЮВАННЯ НА WINDOWS ---
            print(f"\n[Copy] Безпечно копіюємо байти на диск Windows у: {output_dir}")
            
            # Базовий файл .pkl копіюємо завжди
            files_to_copy = [(newest_pkl, output_pkl)]
            
            # Якщо ONNX експорт пройшов успішно — копіюємо і ONNX-файли
            if onnx_success and os.path.exists(temp_encoder_onnx) and os.path.exists(temp_decoder_onnx):
                files_to_copy.append((temp_encoder_onnx, os.path.join(output_dir, "encoder_ft.onnx")))
                files_to_copy.append((temp_decoder_onnx, os.path.join(output_dir, "decoder_step_ft.onnx")))

            for src_path, dst_path in files_to_copy:
                try:
                    with open(src_path, "rb") as f_src:
                        with open(dst_path, "wb") as f_dst:
                            f_dst.write(f_src.read())
                    print(f"[OK] Успішно збережено на Windows: {os.path.basename(dst_path)}")
                except Exception as e:
                    print(f"[Error] Помилка копіювання файлу {os.path.basename(dst_path)}: {e}")
                    
            print("[OK] Всі операції копіювання завершено успішно!")
        else:
            print("[Warning] Навчання завершилось без помилок, але файлів .pkl не виявлено.")
    else:
        print(f"\n[Error] Помилка під час навчання (Exit code: {exit_code}).")

    # Очищаємо тимчасову Linux папку
    try:
        shutil.rmtree(LINUX_TEMP_DIR)
    except Exception:
        pass

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Needle fine-tuning script with optional automatic ONNX export.")
    parser.add_argument("--task", default="ua_en", help="Task directory name (e.g. ua_en, jp_en, neuron)")
    parser.add_argument("--only-train", action="store_true", help="Only train the model and save .pkl, skip ONNX export.")
    args = parser.parse_args()
    
    run_training(task_name=args.task, skip_onnx=args.only_train)