# librarian_ai/training_data/compile_dataset.py
import os
import json
import argparse

# Цей скрипт лежить у librarian_ai/training_data/
BASE_DIR = os.path.dirname(os.path.abspath(__file__))

# Оновлена схема: тепер кожне поле є МАСИВОМ рядків (array of strings)
TOOLS_TEMPLATE = [
    {
        "name": "extract_semantic_roles",
        "description": "Extracts syntactic linguistic roles from the user query.",
        "parameters": {
            "agent": {"type": "array", "items": {"type": "string"}},
            "action": {"type": "array", "items": {"type": "string"}},
            "patient": {"type": "array", "items": {"type": "string"}},
            "instrument": {"type": "array", "items": {"type": "string"}},
            "condition": {"type": "array", "items": {"type": "string"}}
        }
    }
]
TOOLS_STR = json.dumps(TOOLS_TEMPLATE)

def compile_dataset(task_name):
    task_dir = os.path.join(BASE_DIR, task_name)
    raw_path = os.path.join(task_dir, "raw_data.txt")
    output_path = os.path.join(task_dir, "train_data.jsonl")

    if not os.path.exists(raw_path):
        print(f"[Error] Не знайдено сирий текстовий файл за шляхом: {raw_path}")
        return

    print(f"Starting compilation for task: {task_name.upper()}")
    compiled_lines = []
    count = 0

    current_block = {
        "query": "",
        "agent": [],
        "action": [],
        "patient": [],
        "instrument": [],
        "condition": []
    }

    def reset_block():
        return {
            "query": "",
            "agent": [],
            "action": [],
            "patient": [],
            "instrument": [],
            "condition": []
        }

    # Допоміжна функція збереження блоку
    def save_block(block):
        nonlocal count
        if not block["query"]:
            return
        answers_structure = [
            {
                "name": "extract_semantic_roles",
                "arguments": {
                    "agent": block["agent"],
                    "action": block["action"],
                    "patient": block["patient"],
                    "instrument": block["instrument"],
                    "condition": block["condition"]
                }
            }
        ]
        compiled_item = {
            "query": block["query"],
            "tools": TOOLS_STR,
            "answers": json.dumps(answers_structure, ensure_ascii=False)
        }
        compiled_lines.append(json.dumps(compiled_item, ensure_ascii=False))
        count += 1

    with open(raw_path, "r", encoding="utf-8") as f_in:
        for line_num, line in enumerate(f_in, 1):
            line = line.strip()
            if not line:
                continue

            if line == "---":
                save_block(current_block)
                current_block = reset_block()
                continue

            if ":" in line:
                parts = line.split(":", 1)
                # ЗВЕРНІТЬ УВАГУ: робимо ключ нижнім регістром (.lower()) для захисту від "Action:"
                key = parts[0].strip().lower()
                val = parts[1].strip()

                if key == "query":
                    current_block["query"] = val
                elif key in current_block:
                    current_block[key].append(val)
                else:
                    print(f"[Warning] Рядок {line_num}: Невідомий ключ '{parts[0].strip()}'. Пропущено.")

    # ЗАХИСТ: якщо файл закінчився, а роздільник "---" в кінці забули поставити
    if current_block["query"]:
        save_block(current_block)

    with open(output_path, "w", encoding="utf-8") as f_out:
        for cl in compiled_lines:
            f_out.write(cl + "\n")

    print(f"[OK] Успішно скомпільовано {count} блоків у {output_path}!")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Compile raw text blocks to Needle-compatible JSONL format with array support.")
    parser.add_argument("--task", default="ua_en", help="Task directory name (e.g. ua_en, jp_en, neuron)")
    args = parser.parse_args()
    
    compile_dataset(args.task)