using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using SboxAstGraph.Workspace;
using SboxAstGraph.Filtering;
using SboxAstGraph.Analysis;
using SboxAstGraph.Exporters;
using SboxAstGraph.Model;

namespace SboxAstGraph
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var mcpStdout = Console.Out; // Зберігаємо чистий stdout для MCP

            // Якщо запущено у режимі MCP, відправляємо всі текстові логи в stderr, щоб не псувати JSON-потік
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--mode" && i + 1 < args.Length && args[i + 1].Equals("mcp", StringComparison.OrdinalIgnoreCase))
                {
                    Console.SetOut(Console.Error);
                    break;
                }
            }

            Console.WriteLine("=== SboxAstGraph: Статичний аналізатор коду ===");

            SboxAstGraph.Filtering.TypeFilter.IncludeEngineLinks = args.Contains("--engine-links");

            string srcPath = ".";
            string outPath = "./output_test";
            string apiPath = "";
            string mode = "both"; // Допустимі режими: both, user, engine

            // Аргументи для нового пошукового рушія
            string queryCmd = ""; // path, explain, search, cycles
            string arg1 = "";
            string arg2 = "";
            string cachePath = "";
            bool isUndirected = false;

            // Простий та надійний парсинг CLI аргументів
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--src" && i + 1 < args.Length) srcPath = args[++i];
                else if (args[i] == "--out" && i + 1 < args.Length) outPath = args[++i];
                else if (args[i] == "--api" && i + 1 < args.Length) apiPath = args[++i];
                else if (args[i] == "--mode" && i + 1 < args.Length) mode = args[++i].ToLower();
                // Аргументи для запитів до кешу
                else if ((args[i] == "--query" || args[i] == "--cmd") && i + 1 < args.Length) queryCmd = args[++i].ToLower();
                else if (args[i] == "--arg1" && i + 1 < args.Length) arg1 = args[++i];
                else if (args[i] == "--arg2" && i + 1 < args.Length) arg2 = args[++i];
                else if (args[i] == "--cache" && i + 1 < args.Length) cachePath = args[++i];
                else if (args[i] == "--undirected") isUndirected = true;
            }

            // --- РЕЖИМ MCP СЕРВЕРА ДЛЯ ШІ ---
            if (mode == "mcp")
            {
                string userCache = cachePath;
                if (string.IsNullOrEmpty(userCache))
                {
                    userCache = Path.Combine(outPath, "user_code", "vec", "graph.json");
                    if (!File.Exists(userCache))
                    {
                        userCache = Path.Combine(outPath, "vec", "graph.json");
                    }
                }

                // 1. Завантажуємо схему Engine API
                var mcpSchema = await SchemaDownloader.GetLatestSchemaAsync(apiPath);
                Dictionary<string, ApiTypeNode>? engineRegistry = null;

                if (mcpSchema != null)
                {
                    var apiParser = new EngineApiParser();
                    engineRegistry = apiParser.Parse(mcpSchema);
                    Console.Error.WriteLine($"[MCP Setup] Engine API реєстр завантажено ({engineRegistry.Count} типів).");
                }

                // 2. Автоматичний перший аналіз коду користувача, якщо кеш відсутній
                if (!File.Exists(userCache))
                {
                    Console.Error.WriteLine("[MCP Setup] Кеш проєкту не знайдено. Автоматичний перший аналіз...");
                    string userOutDir = Path.Combine(outPath, "user_code");
                    Directory.CreateDirectory(userOutDir);

                    var loader = new ProjectLoader();
                    var sourceFiles = loader.FindSourceFiles(srcPath);
                    var compilation = loader.CreateCompilation(sourceFiles, mcpSchema);

                    var filter = new TypeFilter(mcpSchema);
                    var analyzer = new CodeAnalyzer(filter);
                    var userGraph = analyzer.Analyze(compilation);

                    var exporter = new GraphExporter(userOutDir);
                    exporter.Export(userGraph);

                    userCache = Path.Combine(userOutDir, "vec", "graph.json");
                }

                try
                {
                    Console.Error.WriteLine($"[MCP Setup] Завантаження графу з: {userCache}");
                    var queryEngine = new QueryEngine(userCache, engineRegistry);
                    var librarianClient = new LibrarianClient();
                    var mcpServer = new McpServer(queryEngine, librarianClient, outPath, srcPath, mcpStdout);

                    await mcpServer.ListenAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[MCP Error] Критична помилка MCP сервера: {ex.Message}\n{ex.StackTrace}");
                }

                return;
            }

            // ЯКЩО ЗАПУЩЕНО РЕЖИМ ЗАПИТУ (Миттєве виконання без компіляції Roslyn)
            if (!string.IsNullOrEmpty(queryCmd))
            {
                // Якщо шлях до кешу не вказано, пробуємо знайти його у дефолтній папці виводу
                if (string.IsNullOrEmpty(cachePath))
                {
                    cachePath = Path.Combine(outPath, "user_code", "graph.json");
                }

                try
                {
                    var queryEngine = new QueryEngine(cachePath);
                    string queryResult = "";

                    if (queryCmd == "path")
                    {
                        if (string.IsNullOrEmpty(arg1) || string.IsNullOrEmpty(arg2))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("[Помилка] Для пошуку зв'язку вкажіть '--arg1 <Клас1>' та '--arg2 <Клас2>'");
                            Console.ResetColor();
                            return;
                        }
                        queryResult = queryEngine.FindPath(arg1, arg2, isUndirected);
                    }
                    else if (queryCmd == "metrics" || queryCmd == "weight")
                    {
                        queryResult = queryEngine.GetMetrics();
                    }
                    else if (queryCmd == "cycles" || queryCmd == "loops")
                    {
                        queryResult = queryEngine.FindCycles();
                    }
                    else if (queryCmd == "explain")
                    {
                        if (string.IsNullOrEmpty(arg1))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("[Помилка] Для пояснення класу вкажіть '--arg1 <НазваКласу>'");
                            Console.ResetColor();
                            return;
                        }
                        queryResult = queryEngine.Explain(arg1);
                    }
                    else if (queryCmd == "search" || queryCmd == "query")
                    {
                        if (string.IsNullOrEmpty(arg1))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("[Помилка] Для пошуку вкажіть рядок пошуку через '--arg1 <ПошуковийЗапит>'");
                            Console.ResetColor();
                            return;
                        }

                        // Наш новий семантичний ШІ-пошук через локальний сервіс
                        string activeOutPath = Path.Combine(outPath, "engine_api");
                        if (!Directory.Exists(Path.Combine(activeOutPath, "vec")))
                        {
                            activeOutPath = Path.Combine(outPath, "user_code");
                        }

                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"[C#] Запуск семантичного ШІ-пошуку для запиту: \"{arg1}\"...");
                        Console.ResetColor();

                        var client = new SboxAstGraph.Workspace.LibrarianClient();
                        var response = await client.QuerySemanticAsync(activeOutPath, arg1);

                        if (response != null && response.matches != null && response.matches.Count > 0)
                        {
                            Console.WriteLine($"\n=== ЗНАЙДЕНО НАЙБІЛЬШ СХОЖИХ СУТНОСТЕЙ (Топ-{response.matches.Count}) ===");
                            for (int i = 0; i < response.matches.Count; i++)
                            {
                                var match = response.matches[i];
                                ConsoleColor scoreColor = match.score > 0.85 ? ConsoleColor.Green : ConsoleColor.Yellow;

                                Console.Write($"{i + 1}. ");
                                Console.ForegroundColor = scoreColor;
                                Console.Write($"[{match.score * 100:F1}%]");
                                Console.ResetColor();

                                Console.ForegroundColor = ConsoleColor.White;
                                Console.Write($" {match.fqn} ");
                                Console.ResetColor();

                                Console.ForegroundColor = ConsoleColor.DarkGray;
                                Console.WriteLine($"({match.type})");
                                Console.ResetColor();

                                string noteFileName = $"{match.id.Replace("C:", "").Replace("M:", "").Replace("P:", "").Split('(')[0]}.md";
                                string localNotePath = Path.Combine(activeOutPath, noteFileName);

                                Console.WriteLine($"   Summary: {match.preview}");
                                Console.ForegroundColor = ConsoleColor.Blue;
                                Console.WriteLine($"   File: {localNotePath}");
                                Console.WriteLine();
                                Console.ResetColor();
                            }
                            return; // Перериваємо, бо ми самі вивели результати красивим списком
                        }
                        else
                        {
                            queryResult = "[C#] Семантичних збігів не знайдено або AI-сервіс вимкнено.";
                        }
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[Помилка] Невідомий тип запиту '{queryCmd}'. Допустимі: path, explain, search");
                        Console.ResetColor();
                        return;
                    }

                    Console.WriteLine("\n=== РЕЗУЛЬТАТ ЗАПИТУ ===");
                    Console.WriteLine(queryResult);
                    Console.WriteLine("=========================");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[Критична помилка запиту] {ex.Message}");
                    Console.ResetColor();
                }

                return; // Завершуємо утиліту миттєво, не запускаючи основний аналіз коду
            }

            var stopwatch = Stopwatch.StartNew();

            Console.WriteLine($"[ОК] Шлях до коду: {Path.GetFullPath(srcPath)}");
            Console.WriteLine($"[ОК] Шлях для результатів: {Path.GetFullPath(outPath)}");
            Console.WriteLine($"[ОК] Режим запуску конвеєра: --mode \"{mode}\"");
            Console.WriteLine("---------------------------------------------");

            if (mode != "both" && mode != "user" && mode != "engine")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Помилка] Невірний режим --mode. Допустимі варіанти: both, user, engine");
                Console.ResetColor();
                return;
            }

            // Завантажуємо схему API S&box (вона потрібна для обох систем)
            var schema = await SchemaDownloader.GetLatestSchemaAsync(apiPath);

            // Визначаємо ізольовані вихідні папки для результатів експорту
            string userOutPath = Path.Combine(outPath, "user_code");
            string engineOutPath = Path.Combine(outPath, "engine_api");

            // --- СИСТЕМА 1: Аналіз кастомного коду користувача ---
            if (mode == "both" || mode == "user")
            {
                Console.WriteLine("\n=== Крок 1: Аналіз коду користувача (Система 1) ===");
                Directory.CreateDirectory(userOutPath);

                var loader = new ProjectLoader();
                var sourceFiles = loader.FindSourceFiles(srcPath);
                Console.WriteLine($"Сканування директорії... Знайдено файлів коду (C# / Razor): {sourceFiles.Count}");

                Console.WriteLine("Створення семантичної моделі проєкту (Roslyn Compilation)...");
                var compilation = loader.CreateCompilation(sourceFiles, schema);

                var filter = new TypeFilter(schema);
                var analyzer = new CodeAnalyzer(filter);
                var graph = analyzer.Analyze(compilation);

                Console.WriteLine($"\n--- Експорт вашого коду у: {userOutPath} ---");
                var swUser = System.Diagnostics.Stopwatch.StartNew();
                var exporter = new GraphExporter(userOutPath);
                exporter.Export(graph);
                swUser.Stop();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[C#] Експорт та запис Markdown-файлів завершено за: {swUser.ElapsedMilliseconds} мс!");
                Console.ResetColor();

                await exporter.TriggerSemanticIndexingAsync("SboxUserProject");
            }

            // --- СИСТЕМА 2: Документація та аналіз API двигуна ---
            if (mode == "both" || mode == "engine")
            {
                Console.WriteLine("\n=== Крок 2: Документування API двигуна S&box (Система 2) ===");
                if (schema == null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[Пропущено] Схему api.json не знайдено. Аналіз API двигуна неможливий.");
                    Console.ResetColor();
                }
                else
                {
                    Directory.CreateDirectory(engineOutPath);

                    var engineAnalyzer = new EngineAnalyzer();
                    var apiGraph = engineAnalyzer.Analyze(schema);

                    Console.WriteLine($"\n--- Експорт API рушія у: {engineOutPath} ---");
                    var swEngine = System.Diagnostics.Stopwatch.StartNew();
                    var exporter = new GraphExporter(engineOutPath);
                    exporter.ExportEngineApi(apiGraph, engineAnalyzer);
                    swEngine.Stop();

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[C#] Експорт та запис API-документації завершено за: {swEngine.ElapsedMilliseconds} мс!");
                    Console.ResetColor();

                    await exporter.TriggerSemanticIndexingAsync("SboxEngineAPI");
                }
            }

            stopwatch.Stop();
            Console.WriteLine("---------------------------------------------");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Успішно виконано за: {stopwatch.ElapsedMilliseconds} мс.");
            Console.ResetColor();
            Console.WriteLine("---------------------------------------------");
        }
    }
}