using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SboxAstGraph.Workspace
{
    public class DocumentItem
    {
        public string id { get; set; } = string.Empty;
        public string fqn { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public string text { get; set; } = string.Empty;
    }

    public class IndexRequest
    {
        public string project_id { get; set; } = string.Empty;
        public string out_directory { get; set; } = string.Empty;
        public List<DocumentItem> documents { get; set; } = new();
    }

    public class QueryRequest
    {
        public string out_directory { get; set; } = string.Empty;
        public string query { get; set; } = string.Empty;
        public int max_results { get; set; } = 5;
        public float threshold { get; set; } = 0.0f; // 0.0 = Без порогу відсікання!
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

    public class LibrarianClient
    {
        private readonly HttpClient _client;
        private const string BaseUrl = "http://127.0.0.1:8080";

        public LibrarianClient()
        {
            _client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return Path.GetFullPath(path).Replace('\\', '/');
        }

        public async Task EnsureServiceRunningAsync()
        {
            try
            {
                var response = await _client.GetAsync($"{BaseUrl}/");
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("[C# Bridge] [ОК] ШІ-демон готовий і активний на порту 8080.");
                    return;
                }
            }
            catch
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[C# Bridge] Локальний ШІ-демон офлайн. Автоматичний запуск сервісу...");
                Console.ResetColor();

                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                    // Resolve absolute path to Python executable
                    string pythonExe = "python";
                    string venvPy1 = Path.Combine(baseDir, ".venv", "Scripts", "python.exe");
                    string venvPy2 = Path.Combine(baseDir, "venv", "Scripts", "python.exe");

                    if (File.Exists(venvPy1)) pythonExe = venvPy1;
                    else if (File.Exists(venvPy2)) pythonExe = venvPy2;

                    // Resolve absolute path to Python script
                    string scriptPath = Path.Combine(baseDir, "librarian_ai", "librarian_service.py");
                    if (!File.Exists(scriptPath))
                    {
                        // Fallback to searching relative to current directory
                        scriptPath = Path.GetFullPath("librarian_ai/librarian_service.py");
                    }

                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = pythonExe,
                        Arguments = $"\"{scriptPath}\"",
                        WorkingDirectory = baseDir, // Fix working directory!
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false
                    };

                    System.Diagnostics.Process.Start(startInfo);

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write("[C# Bridge] Завантаження ШІ-моделі у пам'ять ");
                    Console.ResetColor();

                    for (int i = 0; i < 60; i++)
                    {
                        await Task.Delay(1000);
                        Console.Write(".");
                        try
                        {
                            var check = await _client.GetAsync($"{BaseUrl}/");
                            if (check.IsSuccessStatusCode)
                            {
                                Console.WriteLine(" [ОК] ШІ-модель готовий до індексації!");
                                return;
                            }
                        }
                        catch { }
                    }
                    Console.WriteLine("\n[Увага] Перевищено ліміт часу очікування моделі.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n[Помилка] Не вдалося запустити Python: {ex.Message}");
                }
            }
        }

        public async Task<bool> IndexProjectAsync(string projectId, string outDirectory, List<DocumentItem> documents)
        {
            await EnsureServiceRunningAsync();

            string cleanOutDir = NormalizePath(outDirectory);
            string url = $"{BaseUrl}/index";

            var requestPayload = new IndexRequest
            {
                project_id = projectId,
                out_directory = cleanOutDir,
                documents = documents
            };

            try
            {
                Console.WriteLine($"\n[C# Bridge] Передача {documents.Count} блоків коду на векторизацію...");
                string json = JsonSerializer.Serialize(requestPayload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("[C# Bridge] ШІ-модель генерує ембеддинги (векторизація) ");
                Console.ResetColor();

                var responseTask = _client.PostAsync(url, content);

                while (!responseTask.IsCompleted)
                {
                    Console.Write(".");
                    await Task.Delay(1000);
                }
                Console.WriteLine();

                var response = await responseTask;

                if (response.IsSuccessStatusCode)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[C# Bridge] [УСПІХ] Векторний індекс збережено у: {cleanOutDir}");
                    Console.ResetColor();
                    return true;
                }

                string errorMsg = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[C# Bridge] Помилка індексації: {response.StatusCode} - {errorMsg}");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[C# Bridge] Помилка з'єднання: {ex.Message}");
                Console.ResetColor();
            }

            return false;
        }

        public async Task<QueryResponse?> QuerySemanticAsync(string outDirectory, string query, int maxResults = 5)
        {
            await EnsureServiceRunningAsync();

            string cleanOutDir = NormalizePath(outDirectory);
            string url = $"{BaseUrl}/query";

            var requestPayload = new QueryRequest
            {
                out_directory = cleanOutDir,
                query = query,
                max_results = maxResults,
                threshold = 0.0f
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
                Console.WriteLine($"[C# Bridge] Помилка запиту: {ex.Message}");
                Console.ResetColor();
            }

            return null;
        }
    }
}