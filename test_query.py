import urllib.request
import json

url = "http://127.0.0.1:8080/query"

# Твій запит українською
payload = {
    "out_directory": "./output_test/engine_api",
    "query": "вибрати або відсемплити колір"
}

# Відправляємо запит через стандартну бібліотеку Python (без зовнішніх залежностей)
req = urllib.request.Request(
    url, 
    data=json.dumps(payload).encode('utf-8'), 
    headers={'Content-Type': 'application/json'}
)

try:
    with urllib.request.urlopen(req) as response:
        result = json.loads(response.read().decode('utf-8'))
        # Виводимо гарний JSON-результат
        print(json.dumps(result, indent=2, ensure_ascii=False))
except Exception as e:
    print(f"Помилка запиту: {e}")