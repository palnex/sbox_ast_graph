# librarian_ai/run_comparison_test.py
import os
import sys
import json
import time

# Вимикаємо агресивне резервування пам'яті JAX (для стабільності на Windows/WSL):
os.environ["XLA_PYTHON_CLIENT_PREALLOCATE"] = "false"

try:
    from needle import load_checkpoint, SimpleAttentionNetwork, generate, get_tokenizer
except ImportError:
    print("[Error] Пакет needle не знайдено. Будь ласка, переконайтеся, що ви в потрібному оточенні.")
    sys.exit(1)

# Визначаємо шляхи
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
BASE_MODEL_PATH = os.path.join(os.path.dirname(BASE_DIR), "checkpoints", "needle.pkl")
FT_MODEL_PATH = os.path.join(BASE_DIR, "models", "fine_tunes", "ua_en", "needle_ft.pkl")
TOKENIZER_PATH = os.path.join(BASE_DIR, "models", "needle", "tokenizer.model")

# Опис доступних інструментів (змішуємо старі медіа-інструменти та нові Тета-ролі)
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

# Об'єднуємо інструменти в один пул, щоб перевірити якість маршрутизації (routing)
ALL_TOOLS = media_tools + semantic_tools
TOOLS_STR = json.dumps(ALL_TOOLS)

# 10 тестових сценаріїв (змішані запити)
TEST_CASES = [
    "If the player jumps, run the JumpSound function using low pitch",
    "Skip this song please",
    "Make the player volume 85 percent",
    "When the zombie dies, trigger SpawnLoot with gold_chest",
    "Execute the Explode function with high intensity",
    "If health drops to zero, call GameOver with delay",
    "Play the next track",
    "Run the SaveGame method on local drive",
    "If the user is crouching, reduce movement speed to half",
    "Set the media volume level to 30"
]

def run_test_suite(model_path, name, tokenizer):
    print(f"\nЗавантаження моделі [{name}] з {model_path}...")
    if not os.path.exists(model_path):
        print(f"[Error] Не знайдено чекпоінт за шляхом: {model_path}")
        return None
        
    params, config = load_checkpoint(model_path)
    model = SimpleAttentionNetwork(config)
    
    results = []
    
    # Робимо один "теплий" (warm-up) запуск, щоб XLA скомпілював граф і це не псувало метрики часу першого тесту
    print("Прогрів моделі (Warm-up JIT compilation)...")
    try:
        generate(model, params, tokenizer, query="test", tools=TOOLS_STR, stream=False)
    except Exception:
        pass

    print(f"Запуск тестів для [{name}]...")
    for idx, query in enumerate(TEST_CASES, 1):
        start_time = time.perf_counter()
        try:
            response = generate(model, params, tokenizer, query=query, tools=TOOLS_STR, stream=False)
            response_clean = response.strip()
        except Exception as e:
            response_clean = f"Error: {e}"
            
        elapsed_ms = (time.perf_counter() - start_time) * 1000
        
        # Рахуємо токени відповіді
        tokens_count = len(tokenizer.encode(response_clean))
        
        results.append({
            "id": idx,
            "query": query,
            "latency": elapsed_ms,
            "tokens": tokens_count,
            "response": response_clean
        })
    return results

def main():
    print("==================================================")
    print("=== NEEDLE HYBRID COMPARISON BENCHMARK ===")
    print("==================================================")
    
    tokenizer = get_tokenizer(TOKENIZER_PATH)
    
    # 1. Проганяємо тести на Базовій моделі
    base_results = run_test_suite(BASE_MODEL_PATH, "BASE", tokenizer)
    
    # 2. Проганяємо тести на Донавченій моделі
    ft_results = run_test_suite(FT_MODEL_PATH, "FINE-TUNED (UA/EN)", tokenizer)
    
    if not base_results or not ft_results:
        print("[Error] Не вдалося зібрати дані для порівняння.")
        return

    # 3. Вивід порівняльної таблиці
    print("\n" + "="*80)
    print("                       ПОРІВНЯЛЬНА ТАБЛИЦЯ РЕЗУЛЬТАТІВ")
    print("="*80)
    print(f"{'ID':<3} | {'Query (Запит)':<40} | {'Base Model':<14} | {'FT Model':<14}")
    print("-"*80)
    
    for i in range(len(TEST_CASES)):
        q = TEST_CASES[i]
        # Скорочуємо текст запиту для красивого виводу
        q_short = q[:37] + "..." if len(q) > 40 else q
        
        # Оцінюємо правильність виклику (базова часто повертає пустий масив або ламається на нових ролях)
        base_resp = base_results[i]["response"]
        ft_resp = ft_results[i]["response"]
        
        # Перевіряємо чи викликано правильний інструмент
        base_tool = "semantic" if "extract_semantic" in base_resp else "media" if "media" in base_resp else "none"
        ft_tool = "semantic" if "extract_semantic" in ft_resp else "media" if "media" in ft_resp else "none"
        
        print(f"{i+1:<3} | {q_short:<40} | {base_tool:<14} | {ft_tool:<14}")
        
    print("="*80)
    
    # 4. Детальний лог порівняння відповідей
    print("\n=== ДЕТАЛЬНИЙ АНАЛІЗ ВІДПОВІДЕЙ (Side-by-Side) ===")
    for i in range(len(TEST_CASES)):
        print(f"\n[Тест {i+1}] Запит: \"{TEST_CASES[i]}\"")
        print(f" ├─ BASE (Затримка: {base_results[i]['latency']:.1f}ms, {base_results[i]['tokens']} токенів):")
        print(f" │    {base_results[i]['response']}")
        print(f" └─ FINE-TUNED (Затримка: {ft_results[i]['latency']:.1f}ms, {ft_results[i]['tokens']} токенів):")
        print(f"      {ft_results[i]['response']}")
    print("==================================================")

if __name__ == "__main__":
    main()