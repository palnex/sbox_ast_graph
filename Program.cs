using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using SboxAstGraph.Workspace;
using SboxAstGraph.Filtering;
using SboxAstGraph.Analysis;
using SboxAstGraph.Exporters;

namespace SboxAstGraph
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== SboxAstGraph: Статичний аналізатор коду ===");

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
                        queryResult = queryEngine.Search(arg1);
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
                var exporter = new GraphExporter(userOutPath);
                exporter.Export(graph);
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
                    var exporter = new GraphExporter(engineOutPath);
                    exporter.ExportEngineApi(apiGraph, engineAnalyzer); // <- Замінено на analyzer
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