# librarian_ai/test_ft_model.py
import os
# Вимикаємо агресивне резервування 90% відеопам'яті JAX:
#os.environ["XLA_PYTHON_CLIENT_PREALLOCATE"] = "false"
import json
from needle import load_checkpoint, SimpleAttentionNetwork, generate, get_tokenizer

# Оскільки скрипт вже лежить у папці librarian_ai, BASE_DIR вказує прямо на неї!
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
FT_MODEL_PATH = os.path.join(BASE_DIR, "models", "fine_tunes", "ua_en", "needle_ft.pkl")
TOKENIZER_PATH = os.path.join(BASE_DIR, "models", "needle", "tokenizer.model")

def main():
    print("=== ТЕСТУВАННЯ ДОНАВЧЕНОЇ МОДЕЛІ (FINE-TUNED NEEDLE) ===")
    
    if not os.path.exists(FT_MODEL_PATH):
        print(f"[Error] Не знайдено донавчений чекпоінт за шляхом: {FT_MODEL_PATH}")
        return

    # 1. Завантаження моделі
    print("\n[1/3] Завантаження донавчених ваг...")
    params, config = load_checkpoint(FT_MODEL_PATH)
    model = SimpleAttentionNetwork(config)
    tokenizer = get_tokenizer(TOKENIZER_PATH)
    print("[OK] Модель успішно завантажено в пам'ять!")

    # 2. Підготовка тестового запиту (Тета-ролі)
    query = "If the player jumps, run the JumpSound function using low pitch"
    tools = [
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

    print(f"\n[2/3] Тестовий запит: \"{query}\"")

    # 3. Генерація відповіді
    print("\n[3/3] Генерація відповіді через донавчену модель...")
    try:
        tools_str = json.dumps(tools)
        response = generate(model, params, tokenizer, query=query, tools=tools_str, stream=False)
        
        print("\n=== РЕЗУЛЬТАТ ГЕНЕРАЦІЇ ===")
        print(response)
        print("===========================")
    except Exception as e:
        print(f"[Error] Помилка під час генерації: {e}")

if __name__ == "__main__":
    main()