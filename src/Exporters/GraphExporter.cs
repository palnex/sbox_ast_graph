using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SboxAstGraph.Model;

namespace SboxAstGraph.Exporters
{
    public class GraphExporter
    {
        private readonly string _outPath;

        public GraphExporter(string outPath)
        {
            _outPath = outPath;
        }

        public void Export(CodeGraph graph)
        {
            Console.WriteLine("Збереження результатів аналізу...");

            // 1. Експорт у graph.json
            ExportJson(graph);

            // 2. Експорт у Markdown-нотатки для кожного класу
            ExportMarkdownNotes(graph);

            // 3. Експорт в Obsidian Canvas (graph.canvas)
            ExportObsidianCanvas(graph);

            Console.WriteLine($"[ОК] Усі файли успішно збережено в папку: {_outPath}");
        }

        private void ExportJson(CodeGraph graph)
        {
            string jsonPath = Path.Combine(_outPath, "graph.json");

            var exportData = new
            {
                nodes = graph.Nodes.Values.Select(n => new { id = n.Id, file = Path.GetFileName(n.FilePath), @namespace = n.Namespace }),
                links = graph.Edges.Select(e => new { source = e.Source, target = e.Target, type = e.Type, details = e.Details })
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonString = JsonSerializer.Serialize(exportData, options);
            File.WriteAllText(jsonPath, jsonString);
            Console.WriteLine($"  -> Збережено JSON: {jsonPath}");
        }

        private void ExportMarkdownNotes(CodeGraph graph)
        {
            foreach (var node in graph.Nodes.Values)
            {
                string notePath = Path.Combine(_outPath, $"{node.Id}.md");

                // 1. Визначаємо тип ноди (UI чи Логіка)
                bool isUi = node.Namespace == "SboxGeneratedRazorSpace" ||
                            node.FilePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);
                string typeLabel = isUi ? "razor_component" : "class";

                using (var writer = new StreamWriter(notePath))
                {
                    // Очищаємо назву namespace для YAML, щоб уникнути помилок через символи <>
                    string cleanNamespace = string.IsNullOrEmpty(node.Namespace) || node.Namespace == "<global namespace>"
                        ? "global"
                        : node.Namespace.Replace("<", "").Replace(">", "").Trim();

                    // --- СТВОРЮЄМО YAML FRONTMATTER ДЛЯ ШІ ТА ОБСИДІАНУ ---
                    writer.WriteLine("---");
                    writer.WriteLine($"type: {typeLabel}");
                    writer.WriteLine($"namespace: {cleanNamespace}");
                    writer.WriteLine("tags:");
                    if (isUi)
                    {
                        writer.WriteLine("  - user/ui");
                    }
                    else
                    {
                        writer.WriteLine("  - user/logic");
                    }
                    writer.WriteLine("---");
                    writer.WriteLine();
                    // -----------------------------------------------------

                    writer.WriteLine($"# {node.Id}");
                    writer.WriteLine();
                    writer.WriteLine($"**Namespace:** `{cleanNamespace}`  "); // Буде гарно виводити "global" замість "<global namespace>"
                    writer.WriteLine($"**Source:** `{Path.GetFileName(node.FilePath)}`  ");
                    writer.WriteLine();
                    writer.WriteLine("---");
                    writer.WriteLine();

                    // Вихідні зв'язки (Sleek single-line style)
                    writer.WriteLine("## Out");
                    writer.WriteLine();
                    var outgoing = graph.Edges.Where(e => e.Source == node.Id).ToList();
                    if (outgoing.Count > 0)
                    {
                        foreach (var edge in outgoing)
                        {
                            writer.WriteLine($"- ─[{edge.Type}]─> [[{edge.Target}]] `{edge.Details}`");
                        }
                    }
                    else
                    {
                        writer.WriteLine("*None*");
                    }

                    writer.WriteLine();

                    // Вхідні зв'язки (Sleek single-line style)
                    writer.WriteLine("## In");
                    writer.WriteLine();
                    var incoming = graph.Edges.Where(e => e.Target == node.Id).ToList();
                    if (incoming.Count > 0)
                    {
                        foreach (var edge in incoming)
                        {
                            writer.WriteLine($"- [[{edge.Source}]] ─[{edge.Type}]─> `{edge.Details}`");
                        }
                    }
                    else
                    {
                        writer.WriteLine("*None*");
                    }
                }
            }
            Console.WriteLine($"  -> Згенеровано Markdown нотаток з YAML метаданими: {graph.Nodes.Count} шт.");
        }

        private void ExportObsidianCanvas(CodeGraph graph)
        {
            string canvasPath = Path.Combine(_outPath, "graph.canvas");

            // 1. НАЛАШТУВАННЯ ЧИТАНОСТІ (Збільшуємо розміри та відстань)
            int width = 400;       // Було 300 (ширша картка для кращого тексту)
            int height = 220;      // Було 150 (вища картка, щоб поміщався вміст)
            int spacingX = 250;    // Було 120 (більше місця між стовпчиками для стрілок)
            int spacingY = 180;    // Було 100 (більше місця між рядками)

            int cols = (int)Math.Ceiling(Math.Sqrt(graph.Nodes.Count));
            if (cols < 2) cols = 2;

            // 2. АВТО-ВИЗНАЧЕННЯ ШЛЯХУ В ОБСИДІАНІ
            // Нам потрібно знайти назву поточної вихідної папки, щоб Obsidian знав, де лежать файли .md
            // Наприклад, якщо outPath це "C:/vault/sbox_graph", ми маємо вказати в Canvas "sbox_graph/Program.md"
            string folderName = Path.GetFileName(_outPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            var canvasNodes = new List<object>();
            var canvasEdges = new List<object>();
            // Розставляємо вузли по сітці
            var nodesList = graph.Nodes.Values.ToList();
            for (int i = 0; i < nodesList.Count; i++)
            {
                var node = nodesList[i];
                int row = i / cols;
                int col = i % cols;

                int x = col * (width + spacingX);
                int y = row * (height + spacingY);

                // Формуємо простий плоский шлях
                string obsidianFilePath = string.IsNullOrEmpty(folderName) || folderName == "."
                    ? $"{node.Id}.md"
                    : $"{folderName}/{node.Id}.md";

                canvasNodes.Add(new
                {
                    id = $"node_{node.Id}",
                    type = "file",
                    file = obsidianFilePath,
                    x = x,
                    y = y,
                    width = width,
                    height = height
                });
            }

            // Створюємо зв'язки (стрілки)
            for (int i = 0; i < graph.Edges.Count; i++)
            {
                var edge = graph.Edges[i];
                canvasEdges.Add(new
                {
                    id = $"edge_{i}",
                    fromNode = $"node_{edge.Source}",
                    toNode = $"node_{edge.Target}",
                    label = $"{edge.Type}: {edge.Details}"
                });
            }

            var canvasData = new
            {
                nodes = canvasNodes,
                edges = canvasEdges
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string canvasString = JsonSerializer.Serialize(canvasData, options);
            File.WriteAllText(canvasPath, canvasString);
            Console.WriteLine($"  -> Збережено покращений Obsidian Canvas: {canvasPath}");
        }

        public void ExportEngineApi(CodeGraph graph, Dictionary<string, ApiTypeNode> registry)
        {
            Console.WriteLine("Запуск розумного експорту API документації S&box...");

            // 1. Експортуємо стандартний graph.json та graph.canvas
            ExportJson(graph);
            ExportObsidianCanvas(graph);

            // 2. Генеруємо надбагаті Markdown-нотатки на основі моделі API
            foreach (var node in graph.Nodes.Values)
            {
                string notePath = Path.Combine(_outPath, $"{node.Id}.md"); // Запис прямо в корінь папки _outPath

                // Шукаємо багаті метадані типу в реєстрі за його Namespace.ClassName
                string lookupKey = string.IsNullOrEmpty(node.Namespace) || node.Namespace == "Sandbox"
                    ? $"Sandbox.{node.Id}"
                    : $"{node.Namespace}.{node.Id}";

                // Спробуємо знайти також просто за повним або коротким ім'ям
                var richType = registry.Values.FirstOrDefault(t =>
                    string.Equals(t.Name, node.Id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.FullName, lookupKey, StringComparison.OrdinalIgnoreCase));

                using (var writer = new StreamWriter(notePath))
                {
                    writer.WriteLine($"# {node.Id}");
                    writer.WriteLine();
                    writer.WriteLine($"**Простір імен:** `{node.Namespace}`  ");
                    writer.WriteLine($"**Джерело:** `S&box Engine API`  ");
                    writer.WriteLine();

                    if (richType != null && !string.IsNullOrEmpty(richType.Summary))
                    {
                        writer.WriteLine("> [!info] Опис");
                        writer.WriteLine($"> {richType.Summary.Replace("\n", "\n> ")}");
                        writer.WriteLine();
                    }

                    writer.WriteLine("---");
                    writer.WriteLine();

                    if (richType != null)
                    {
                        // А. Таблиця полів
                        var publicFields = richType.Fields.Values.Where(f => f.IsPublic).ToList();
                        if (publicFields.Count > 0)
                        {
                            writer.WriteLine("## Fields (Поля)");
                            writer.WriteLine("| Тип | Назва | Опис |");
                            writer.WriteLine("| --- | --- | --- |");
                            foreach (var field in publicFields)
                            {
                                string summary = string.IsNullOrEmpty(field.Summary) ? "-" : field.Summary.Replace("\r", "").Replace("\n", " ");
                                writer.WriteLine($"| `{field.FieldType}` | **{field.Name}** | {summary} |");
                            }
                            writer.WriteLine();
                        }

                        // Б. Таблиця властивостей
                        var publicProps = richType.Properties.Values.Where(p => p.IsPublic).ToList();
                        if (publicProps.Count > 0)
                        {
                            writer.WriteLine("## Properties (Властивості)");
                            writer.WriteLine("| Тип | Назва | Опис |");
                            writer.WriteLine("| --- | --- | --- |");
                            foreach (var prop in publicProps)
                            {
                                string summary = string.IsNullOrEmpty(prop.Summary) ? "-" : prop.Summary.Replace("\r", "").Replace("\n", " ");
                                writer.WriteLine($"| `{prop.PropertyType}` | **{prop.Name}** | {summary} |");
                            }
                            writer.WriteLine();
                        }

                        // В. Таблиця методів та перевантажень
                        var publicMethods = richType.Methods.Values.Where(m => m.IsPublic).ToList();
                        if (publicMethods.Count > 0)
                        {
                            writer.WriteLine("## Methods (Методи)");
                            writer.WriteLine("| Сигнатура | Опис |");
                            writer.WriteLine("| --- | --- |");
                            foreach (var method in publicMethods)
                            {
                                string summary = string.IsNullOrEmpty(method.Summary) ? "-" : method.Summary.Replace("\r", "").Replace("\n", " ");
                                string @static = method.IsStatic ? "static " : "";
                                string @params = string.Join(", ", method.Parameters.Select(p => $"{p.ParameterType} {p.Name}"));
                                string signature = $"{@static}{method.ReturnType} {method.Name}({@params})";

                                writer.WriteLine($"| `{signature}` | {summary} |");
                            }
                            writer.WriteLine();
                        }
                    }

                    // Г. Локальні архітектурні зв'язки в базі знань
                    writer.WriteLine("## Dependencies");
                    writer.WriteLine();

                    // Вихідні
                    writer.WriteLine("### Outgoing:");
                    var outgoing = graph.Edges.Where(e => e.Source == node.Id).ToList();
                    if (outgoing.Count > 0)
                    {
                        foreach (var edge in outgoing)
                        {
                            writer.WriteLine($"- ─[{edge.Type}]─> [[{edge.Target}]] `({edge.Details})`");
                        }
                    }
                    else
                    {
                        writer.WriteLine("*Немає вихідних логічних зв'язків.*");
                    }

                    writer.WriteLine();

                    // Вхідні
                    writer.WriteLine("### Incoming (Хто використовує цей тип):");
                    var incoming = graph.Edges.Where(e => e.Target == node.Id).ToList();
                    if (incoming.Count > 0)
                    {
                        foreach (var edge in incoming)
                        {
                            writer.WriteLine($"- [[{edge.Source}]] ─[{edge.Type}]─> `({edge.Details})`");
                        }
                    }
                    else
                    {
                        writer.WriteLine("*Ніхто не посилається на цей тип безпосередньо.*");
                    }
                }
            }

            Console.WriteLine($"  -> Згенеровано надбагатих Markdown нотаток API: {graph.Nodes.Count} шт.");
        }
    }
}