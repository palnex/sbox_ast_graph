import os
import json
import uvicorn
import numpy as np
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import List, Dict, Any
from sentence_transformers import SentenceTransformer
from turbovec import IdMapIndex
import time
import threading

app = FastAPI(title="SboxAstGraph Granite-TurboVec Service", version="1.1")

# --- СИСТЕМНІ КОНФІГУРАЦІЇ ---
DIMENSION = 384  # Фіксована розмірність для ibm-granite-97m
BIT_WIDTH = 4    # 4-бітне квантування для ідеального балансу точності/пам'яті

class LocalEmbedder:
    def __init__(self):
        print("[AI] Loading IBM Granite-97M-Multilingual-R2 model on CPU...")
        # Завантажуємо модель від IBM. SentenceTransformers самостійно скачає її при першому запусті (~190MB)
        self.model = SentenceTransformer("ibm-granite/granite-embedding-97m-multilingual-r2")
        print("[AI] IBM Granite model loaded successfully!")

    def get_embedding(self, text: str) -> np.ndarray:
        # Для кращої точності пошуку Granite-R2 рекомендує префікс 'passage: ' для документів
        # та 'query: ' для запитів. Ми додамо його нижче при виклику.
        return self.model.encode(text, convert_to_numpy=True).astype(np.float32)

embedder = None
# Активний індекс в пам'яті
active_index = None
# Активна мапа відповідності ID -> FQN
active_map: Dict[str, Dict[str, Any]] = {}
# Поточний шлях до папки виводу
current_out_dir = ""

# Час останньої активності (ініціалізуємо поточним часом)
last_activity_time = time.time()

def idle_shutdown_checker():
    """
    Фоновий потік, який кожну хвилину перевіряє час бездіяльності
    """
    global last_activity_time
    IDLE_LIMIT = 1800  # 30 хвилин у секундах (можеш змінити на 300 для 5 хвилин)
    
    while True:
        time.sleep(30) # Перевірка кожні 30 секунд
        if time.time() - last_activity_time > IDLE_LIMIT:
            print("\n[AI] Роботу завершено (бездіяльність 15 хв). Автоматичне вимкнення сервісу. Бувай!")
            os._exit(0)

@app.on_event("startup")
def startup_event():
    global embedder
    embedder = LocalEmbedder()
    # --- ЗАПУСК ТАЙМЕРА АВТО-ВИМКНЕННЯ ---
    threading.Thread(target=idle_shutdown_checker, daemon=True).start()

# --- СХЕМИ ДАНИХ ---

class DocumentItem(BaseModel):
    id: str  # Наш лінгвістичний ID (наприклад, 'M:Sandbox.PlayerController.Jump')
    fqn: str # Назва класу чи методу
    type: str # 'class', 'method', 'property', 'enum', 'attribute'
    text: str # Текст опису для векторизації

class IndexRequest(BaseModel):
    project_id: str
    out_directory: str  # Шлях куди C# хоче зберегти файли (наприклад, './output_test')
    documents: List[DocumentItem]

class QueryRequest(BaseModel):
    out_directory: str  # Шлях звідки читати індекс при запиті
    query: str

# --- API МЕТОДИ ---

@app.post("/index")
def index_documents(payload: IndexRequest):
    """
    Генерує вектори через IBM Granite, стискає їх через TurboQuant
    та записує .tvim і semantic_map.json у вибрану користувачем папку.
    """
    global last_activity_time
    last_activity_time = time.time()

    if not embedder:
        raise HTTPException(status_code=500, detail="Embedder model not initialized")
    
    out_dir = payload.out_directory
    os.makedirs(out_dir, exist_ok=True)

    print(f"[Librarian] Starting semantic indexing for project: {payload.project_id}")
    print(f"[Librarian] Target output directory: {out_dir}")

    # 1. Створюємо свіжий індекс TurboVec
    index = IdMapIndex(dim=DIMENSION, bit_width=BIT_WIDTH)
    mapping: Dict[str, Dict[str, Any]] = {}

    texts = []
    u64_ids = []

    # Ми мапимо рядкові лінгвістичні ID на числові u64 для TurboVec
    for i, doc in enumerate(payload.documents):
        numeric_id = 10000 + i
        u64_ids.append(numeric_id)
        
        # Додаємо рекомендований IBM префікс для документів бази знань
        texts.append(f"passage: {doc.text}")
        
        # Зберігаємо відповідність у мапу
        mapping[str(numeric_id)] = {
            "id": doc.id,
            "fqn": doc.fqn,
            "type": doc.type,
            "text": doc.text
        }

    if not texts:
        return {"status": "empty", "message": "No documents provided for indexing."}

    # 2. Генеруємо ембеддінги через IBM Granite (це займе лічені секунди на CPU)
    print(f"[Librarian] Vectorizing {len(texts)} documents...")
    embeddings = embedder.get_embedding(texts)

    # 3. Додаємо вектори в індекс із квантуванням TurboQuant
    index.add_with_ids(embeddings, np.array(u64_ids, dtype=np.uint64))

    # 4. Записуємо файли безпосередньо в папку виводу користувача
    vec_dir = os.path.join(out_dir, "vec")
    os.makedirs(vec_dir, exist_ok=True)

    index_path = os.path.join(vec_dir, "semantic_index.tvim")
    map_path = os.path.join(vec_dir, "semantic_map.json")

    index.write(index_path)
    with open(map_path, "w", encoding="utf-8") as f:
        json.dump(mapping, f, ensure_ascii=False, indent=2)

    print(f"[Librarian] Index saved successfully: {index_path} ({len(texts)} items)")
    return {"status": "ok", "indexed_count": len(texts), "output_dir": out_dir}

@app.get("/")
def health_check():
    """
    Легкий пінг-ендпоінт для C# оркестратора
    """
    return {"status": "alive"}

@app.post("/query")
def query_semantic(payload: QueryRequest):
    """
    Завантажує індекс з вказаної папки (якщо він ще не завантажений) 
    та робить надшвидкий семантичний пошук.
    """
    global last_activity_time
    last_activity_time = time.time()

    global active_index, active_map, current_out_dir

    if not embedder:
        raise HTTPException(status_code=500, detail="Embedder model not initialized")

    out_dir = payload.out_directory
    vec_dir = os.path.join(out_dir, "vec")
    index_path = os.path.join(vec_dir, "semantic_index.tvim")
    map_path = os.path.join(vec_dir, "semantic_map.json")

    # Перевіряємо наявність індексу на диску
    if not os.path.exists(index_path) or not os.path.exists(map_path):
        raise HTTPException(status_code=404, detail=f"Index not found in: {out_dir}. Run indexing first.")

    # Ліниво завантажуємо індекс у пам'ять, тільки якщо шлях змінився
    if current_out_dir != out_dir:
        print(f"[Librarian] Loading semantic index into RAM from: {out_dir}")
        active_index = IdMapIndex.load(index_path)
        with open(map_path, "r", encoding="utf-8") as f:
            active_map = json.load(f)
        current_out_dir = out_dir

    # Додаємо рекомендований IBM префікс для пошукових запитів
    query_text = f"query: {payload.query.strip()}"
    
    # 1. Генеруємо вектор запиту (1x384 f32)
    query_embedding = embedder.get_embedding([query_text])

    # 2. Виконуємо блискавичний пошук через TurboVec
    scores, result_ids = active_index.search(query_embedding, k=5)

    # 3. Розкодовуємо результати через нашу семантичну мапу
    matches = []
    for score, numeric_id in zip(scores[0], result_ids[0]):
        str_id = str(numeric_id)
        if str_id in active_map:
            meta = active_map[str_id]
            matches.append({
                "id": meta["id"],
                "fqn": meta["fqn"],
                "type": meta["type"],
                "score": float(score),
                "preview": meta["text"][:120] + "..."
            })

    return {
        "query": payload.query,
        "matches": matches
    }

if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=8080)