# librarian_ai/run_demo.py
import json
from parser import NeedleParser

def test_modular_parser():
    print("=== ДЕМОНСТРАЦІЯ ЛОКАЛЬНОГО NEEDLE PARSER ===")
    
    # Ініціалізація парсера. Він сам усе перевірить, створить папки та скачає моделі!
    parser = NeedleParser()

    query = "Skip this track and play the next song in the player"
    tools = [
        {
            "name": "media_next_track",
            "description": "Plays the next track in the active media player.",
            "parameters": {}
        }
    ]

    print(f"\n[Запит]: {query}")
    print("[Парсинг...] Розрахунок локального ONNX графу на CPU...")
    
    response = parser.parse(query, tools)
    
    print("\n=== ВІДПОВІДЬ ПАРСЕРА (Чистий JSON) ===")
    print(response)
    print("=========================================")

if __name__ == "__main__":
    test_modular_parser()