import os
from sentence_transformers import SentenceTransformer

MODEL_NAME = "ibm-granite/granite-embedding-97m-multilingual-r2"
SAVE_DIR = os.path.join(os.path.dirname(__file__), "models", "granite")

def main():
    print(f"[Model Downloader] Downloading '{MODEL_NAME}' to '{SAVE_DIR}'...")
    os.makedirs(SAVE_DIR, exist_ok=True)
    model = SentenceTransformer(MODEL_NAME)
    model.save(SAVE_DIR)
    print("[Model Downloader] SUCCESS! Model weights saved locally. You can now use MCP Server offline.")

if __name__ == "__main__":
    main()