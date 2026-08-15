import os
import json
import uvicorn
import numpy as np
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import List, Dict, Any, Optional
from sentence_transformers import SentenceTransformer
from turbovec import IdMapIndex
import time
import threading
import pickle

app = FastAPI(title="SboxAstGraph Granite-TurboVec Service", version="1.2")

# --- СИСТЕМНІ КОНФІГУРАЦІЇ ---
DIMENSION = 384  # Фіксована розмірність для ibm-granite-97m
BIT_WIDTH = 4    # 4-бітне квантування для ідеального балансу точності/пам'яті

# --- LOCAL MODEL SETUP (STRICT OFFLINE) ---
LOCAL_MODEL_DIR = os.path.join(os.path.dirname(__file__), "models", "granite")

class LocalEmbedder:
    def __init__(self):
        if not os.path.exists(LOCAL_MODEL_DIR) or len(os.listdir(LOCAL_MODEL_DIR)) == 0:
            raise RuntimeError(
                f"Granite model directory missing at '{LOCAL_MODEL_DIR}'. "
                f"Run 'python librarian_ai/download_model.py' first!"
            )

        print(f"[AI] Loading local Granite model from: {LOCAL_MODEL_DIR}")
        self.model = SentenceTransformer(LOCAL_MODEL_DIR)
        print("[AI] Embedding service initialized successfully.")

    def get_embedding(self, text: List[str]) -> np.ndarray:
        return self.model.encode(text, convert_to_numpy=True).astype(np.float32)

embedder = None
active_index = None
active_map: Dict[str, Dict[str, Any]] = {}
current_out_dir = ""

last_activity_time = time.time()

def idle_shutdown_checker():
    """
    Фоновий потік, який кожну хвилину перевіряє час бездіяльності
    """
    global last_activity_time
    IDLE_LIMIT = 1800  # 30 хвилин у секундах
    
    while True:
        time.sleep(30)
        if time.time() - last_activity_time > IDLE_LIMIT:
            print("\n[AI] Роботу завершено (бездіяльність 30 хв). Автоматичне вимкнення сервісу. Бувай!")
            os._exit(0)

@app.on_event("startup")
def startup_event():
    global embedder
    embedder = LocalEmbedder()
    threading.Thread(target=idle_shutdown_checker, daemon=True).start()

# --- СХЕМИ ДАНИХ ---

class DocumentItem(BaseModel):
    id: str
    fqn: str
    type: str
    text: str

class IndexRequest(BaseModel):
    project_id: str
    out_directory: str
    documents: List[DocumentItem]

class QueryRequest(BaseModel):
    out_directory: str
    query: str
    max_results: Optional[int] = 5
    threshold: Optional[float] = 0.0

# --- API МЕТОДИ ---

@app.post("/index")
def index_documents(payload: IndexRequest):
    global last_activity_time
    last_activity_time = time.time()

    if not embedder:
        raise HTTPException(status_code=500, detail="Embedder model not initialized")
    
    out_dir = payload.out_directory
    os.makedirs(out_dir, exist_ok=True)

    print(f"[Librarian] Starting semantic indexing for project: {payload.project_id}")
    print(f"[Librarian] Target output directory: {out_dir}")

    index = IdMapIndex(dim=DIMENSION, bit_width=BIT_WIDTH)
    mapping: Dict[str, Dict[str, Any]] = {}

    texts = []
    u64_ids = []

    for i, doc in enumerate(payload.documents):
        numeric_id = 10000 + i
        u64_ids.append(numeric_id)
        
        texts.append(f"passage: {doc.text}")
        
        mapping[str(numeric_id)] = {
            "id": doc.id,
            "fqn": doc.fqn,
            "type": doc.type,
            "text": doc.text
        }

    if not texts:
        return {"status": "empty", "message": "No documents provided for indexing."}

    print(f"[Librarian] Vectorizing {len(texts)} documents via IBM Granite...")
    embeddings = embedder.get_embedding(texts)

    index.add_with_ids(embeddings, np.array(u64_ids, dtype=np.uint64))

    vec_dir = os.path.join(out_dir, "vec")
    os.makedirs(vec_dir, exist_ok=True)

    index_path = os.path.join(vec_dir, "semantic_index.tvim")
    map_path = os.path.join(vec_dir, "semantic_map.json")

    index.write(index_path)
    with open(map_path, "w", encoding="utf-8") as f:
        json.dump(mapping, f, ensure_ascii=False, indent=2)
    
    # Бінарний кеш для завантаження 15k за 0.1 секунди!
    map_path_pkl = os.path.join(vec_dir, "semantic_map.pkl")
    with open(map_path_pkl, "wb") as f:
        pickle.dump(mapping, f)

    print(f"[Librarian] Index saved successfully: {index_path} ({len(texts)} items)")
    return {"status": "ok", "indexed_count": len(texts), "output_dir": out_dir}

@app.get("/")
def health_check():
    return {"status": "alive"}

@app.post("/query")
def query_semantic(payload: QueryRequest):
    global last_activity_time
    last_activity_time = time.time()

    global active_index, active_map, current_out_dir

    if not embedder:
        raise HTTPException(status_code=500, detail="Embedder model not initialized")

    base_dir = payload.out_directory

    # Direct lookup for index binary files
    possible_dirs = [
        os.path.join(base_dir, "vec"),
        base_dir,
        os.path.join(base_dir, "user_code", "vec"),
        os.path.join(base_dir, "engine_api", "vec")
    ]

    index_path, map_path = None, None
    for d in possible_dirs:
        idx_p = os.path.join(d, "semantic_index.tvim")
        map_p = os.path.join(d, "semantic_map.json")
        if os.path.exists(idx_p) and os.path.exists(map_p):
            index_path, map_path = idx_p, map_p
            break

    if not index_path or not map_path:
        raise HTTPException(status_code=404, detail=f"Index binary files not found in: {base_dir}")

    if current_out_dir != index_path:
            print(f"[Librarian] Loading semantic index into RAM from: {index_path}")
            active_index = IdMapIndex.load(index_path)
            
            map_path_pkl = map_path.replace(".json", ".pkl")
            if os.path.exists(map_path_pkl):
                print(f"[Librarian] Fast-loading binary map (.pkl)...")
                with open(map_path_pkl, "rb") as f:
                    active_map = pickle.load(f)
            else:
                print(f"[Librarian] First time load: Reading 50MB JSON and creating fast .pkl cache...")
                with open(map_path, "r", encoding="utf-8") as f:
                    active_map = json.load(f)
                
                # Автоматично зберігаємо .pkl для надшвидких наступних запусків (0.08s)
                try:
                    with open(map_path_pkl, "wb") as f:
                        pickle.dump(active_map, f)
                    print(f"[Librarian] Saved fast binary cache to: {map_path_pkl}")
                except Exception as e:
                    print(f"[Librarian] Warning saving pkl: {e}")

            current_out_dir = index_path

    query_text = f"query: {payload.query.strip()}"
    query_embedding = embedder.get_embedding([query_text])

    k_val = payload.max_results if payload.max_results and payload.max_results > 0 else 5
    min_thresh = payload.threshold if payload.threshold is not None else 0.0

    scores, result_ids = active_index.search(query_embedding, k=k_val)

    matches = []
    for score, numeric_id in zip(scores[0], result_ids[0]):
        float_score = float(score)
        if float_score < min_thresh:
            continue

        str_id = str(numeric_id)
        if str_id in active_map:
            meta = active_map[str_id]
            matches.append({
                "id": meta["id"],
                "fqn": meta["fqn"],
                "type": meta["type"],
                "score": float_score,
                "preview": meta["text"]
            })

    return {
        "query": payload.query,
        "matches": matches
    }

if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=8080)