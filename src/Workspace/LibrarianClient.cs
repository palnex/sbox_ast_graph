using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SboxAstGraph.Workspace
{
    // --- КЛАСИ ДЛЯ СЕРІАЛІЗАЦІЇ (КОНТРАКТИ) ---

    public class DocumentItem
    {
        public string id { get; set; } = string.Empty;       // Наприклад, "M:Sandbox.PlayerController.Jump"
        public string fqn { get; set; } = string.Empty;      // Повне ім'я
        public string type { get; set; } = string.Empty;     // "class", "method", "property"
        public string text { get; set; } = string.Empty;     // Текст опису для векторизації
    }

    public class IndexRequest
    {
        public string project_id { get; set; } = string.Empty;
        public string out_directory { get; set; } = string.Empty; // Шлях, куди зберегти .tvim
        public List<DocumentItem> documents { get; set; } = new();
    }

    public class QueryRequest
    {
        public string out_directory { get; set; } = string.Empty;
        public string query { get; set; } = string.Empty;
    }

    public class QueryResponseMatch
    {
        public string id { get; set; } = string.Empty;
        public string fqn { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public double score { get; set; }
        public string preview { get; set; } = string.Empty;
    }

    public class QueryResponse
    {
        public string query { get; set; } = string.Empty;
        public List<QueryResponseMatch> matches { get; set; } = new();
    }

    // --- КЛІЄНТ З'ЄДНАННЯ ---

    public class LibrarianClient
    {
        private readonly HttpClient _client;
        private const string BaseUrl = "http://127.0.0.1:8080";

        public LibrarianClient()
        {
            _client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) }; // Великий таймаут для першої індексації
        }

        /// <summary>
        /// Автоматично перевіряє, чи запущений ШІ-демон. Якщо ні — приховано запускає його у фоні.
        /// </summary>
        private async Task EnsureServiceRunningAsync()
        {
            try
            {
                // Пробуємо зробити швидкий пінг
                var response = await _client.GetAsync($"{BaseUrl}/");
                if (response.IsSuccessStatusCode) return; // Сервіс уже активний, усе супер!
            }
            catch
            {
                // Якщо зловили помилку з'єднання — сервіс вимкнено. Запускаємо його!
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[C# Bridge] Локальний ШІ-демон офлайн. Автоматичний фоновий запуск сервісу...");
                Console.ResetColor();

                try
                {
                    // Шукаємо шлях до Python всередині venv (віртуального середовища)
                    string pythonExe = "python";
                    if (File.Exists(".venv/Scripts/python.exe")) pythonExe = Path.GetFullPath(".venv/Scripts/python.exe");
                    else if (File.Exists("venv/Scripts/python.exe")) pythonExe = Path.GetFullPath("venv/Scripts/python.exe");
                    else if (File.Exists("librarian_ai/venv/Scripts/python.exe")) pythonExe = Path.GetFullPath("librarian_ai/venv/Scripts/python.exe");
                    else if (File.Exists("librarian_ai/.venv/Scripts/python.exe")) pythonExe = Path.GetFullPath("librarian_ai/.venv/Scripts/python.exe");

                    Console.WriteLine($"[C# Bridge] Використовується Python: {pythonExe}");

                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = pythonExe,
                        Arguments = "librarian_ai/librarian_service.py",
                        UseShellExecute = false,
                        CreateNoWindow = true, // Повністю приховане вікно!
                        RedirectStandardOutput = false,
                        RedirectStandardError = false
                    };

                    System.Diagnostics.Process.Start(startInfo);

                    // Очікуємо запуску та завантаження моделей (робимо пінг кожну секунду, ліміт 15 сек)
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write("[C# Bridge] Завантаження ШІ-моделі IBM Granite у пам'ять ");
                    Console.ResetColor();

                    for (int i = 0; i < 60; i++) // Збільшено до 60 секунд (1 хв)
                    {
                        await Task.Delay(1000);
                        Console.Write(".");
                        try
                        {
                            var check = await _client.GetAsync($"{BaseUrl}/");
                            if (check.IsSuccessStatusCode)
                            {
                                Console.WriteLine(" [ОК] ШІ-демон готовий до роботи!");
                                return;
                            }
                        }
                        catch { /* ігноруємо помилки підключення під час запуску */ }
                    }
                    Console.WriteLine("\n[Увага] Перевищено ліміт часу очікування моделі. Спробуйте запустити 'librarian_service.py' вручну.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n[Помилка] Не вдалося автоматично запустити Python: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Відправляє зібрані документи на індексацію до локального Python-демона.
        /// </summary>
        public async Task<bool> IndexProjectAsync(string projectId, string outDirectory, List<DocumentItem> documents)
        {
            // --- ВСТАВ ЦЕЙ РЯДОК НА ПОЧАТКУ ---
            await EnsureServiceRunningAsync();

            string url = $"{BaseUrl}/index";
            var requestPayload = new IndexRequest
            {
                project_id = projectId,
                out_directory = outDirectory,
                documents = documents
            };

            try
            {
                Console.WriteLine($"[C# Bridge] Надсилання {documents.Count} документів на локальний AI-сервіс...");
                string json = JsonSerializer.Serialize(requestPayload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // --- ЖИВИЙ ІНДИКАТОР ПРОГРЕСУ ---
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("[C# Bridge] ШІ-модель IBM Granite генерує ембеддінги (це може зайняти час) ");
                Console.ResetColor();

                var responseTask = _client.PostAsync(url, content);

                // Поки завдання виконується в фоні, кожну секунду малюємо крапку в консолі
                while (!responseTask.IsCompleted)
                {
                    Console.Write(".");
                    await Task.Delay(1000);
                }
                Console.WriteLine(); // Перехід на новий рядок після завершення

                var response = await responseTask;

                if (response.IsSuccessStatusCode)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[C# Bridge] Індексація завершена! Векторний індекс збережено в робочу папку.");
                    Console.ResetColor();
                    return true;
                }

                string errorMsg = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[C# Bridge] Помилка індексації: {response.StatusCode} - {errorMsg}");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[C# Bridge] Не вдалося з'єднатися з Python AI-сервісом: {ex.Message}");
                Console.WriteLine("  -> Переконайтеся, що 'librarian_service.py' запущений та працює на порту 8080.");
                Console.ResetColor();
            }

            return false;
        }

        /// <summary>
        /// Робить швидкий семантичний запит до активного індексу.
        /// </summary>
        public async Task<QueryResponse?> QuerySemanticAsync(string outDirectory, string query)
        {
            // --- ВСТАВ ЦЕЙ РЯДОК НА ПОЧАТКУ ---
            await EnsureServiceRunningAsync();

            string url = $"{BaseUrl}/query";
            var requestPayload = new QueryRequest
            {
                out_directory = outDirectory,
                query = query
            };

            try
            {
                string json = JsonSerializer.Serialize(requestPayload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    string responseJson = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<QueryResponse>(responseJson);
                }

                string errorMsg = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[C# Bridge] Помилка запиту: {response.StatusCode} - {errorMsg}");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[C# Bridge] Помилка зв'язку під час запиту: {ex.Message}");
                Console.ResetColor();
            }

            return null;
        }
    }
}