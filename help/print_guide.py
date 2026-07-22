# help/print_guide.py
import os

# Оскільки файл лежить у help/, його батьківська папка — це корінь проєкту
HELP_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(HELP_DIR)
DATA_DIR = os.path.join(PROJECT_ROOT, "librarian_ai", "training_data")

print("==================================================================")
print("     NEEDLE MODEL CONVERTER & TRAINING HELPER (2026)")
print("==================================================================")
print("[OK] Віртуальне оточення (.venv_wsl_onnx) активовано!")
print("Робоча директорія (корінь проєкту): /mnt/c/Users/yenro/Desktop/sbox_ast_graph")
print("------------------------------------------------------------------")

# Скануємо наявні папки з даними всередині librarian_ai/training_data/
print("\n[Наявні таски у папці training_data/]:")
if os.path.exists(DATA_DIR):
    tasks = [d for d in os.listdir(DATA_DIR) if os.path.isdir(os.path.join(DATA_DIR, d))]
    if tasks:
        for t in tasks:
            print(f"  • {t}")
    else:
        print("  (Папка порожня. Створіть підпапку, наприклад ua_en)")
else:
    print(f"  (Папку {DATA_DIR} не знайдено)")

print("\n[ГОТОВІ КОМАНДИ ДЛЯ КОПІЮВАННЯ]:")
print("------------------------------------------------------------------")
print("1) НАВЧАННЯ + автоматичний ONNX експорт (оберіть ваш таск):")
print("   python3 librarian_ai/train.py --task ua_en")
print("   # або для іншого таска, якщо ви створили папку:")
print("   # python3 librarian_ai/train.py --task <назва_папки>")
print("------------------------------------------------------------------")
print("2) Тільки НАВЧАННЯ (зберегти тільки .pkl без ONNX):")
print("   python3 librarian_ai/train.py --task ua_en --only-train")
print("------------------------------------------------------------------")
print("3) Тільки ЕКСПОРТ готового .pkl в ONNX (без навчання):")
print("   python3 librarian_ai/export_to_onnx.py \\")
print("     --pkl librarian_ai/models/fine_tunes/ua_en/needle_ft.pkl \\")
print("     --encoder-out librarian_ai/models/fine_tunes/ua_en/encoder_ft.onnx \\")
print("     --decoder-out librarian_ai/models/fine_tunes/ua_en/decoder_step_ft.onnx")
print("==================================================================")
print("Скопіюйте команду вище, вставте її сюди та натисніть Enter:")