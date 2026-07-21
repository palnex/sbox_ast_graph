using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SboxAstGraph.Model;
using SboxAstGraph.Analysis;
using SboxAstGraph.Workspace;
using System.Threading.Tasks;

namespace SboxAstGraph.Exporters
{
    public class GraphExporter
    {
        private readonly string _outPath;

        // --- НОВІ ПОЛЯ ДЛЯ ШІ-ІНДЕКСАЦІЇ ---
        private readonly List<DocumentItem> _chunksToIndex = new();
        private readonly LibrarianClient _librarianClient = new();

        public GraphExporter(string outPath)
        {
            _outPath = outPath;
        }

        public void Export(CodeGraph graph)
        {
            Console.WriteLine("Saving project graph results...");
            ExportJson(graph);
            ExportMarkdownNotes(graph);
            ExportObsidianCanvas(graph);
            Console.WriteLine($"[OK] User code graph saved to: {_outPath}");
        }

        private void ExportJson(CodeGraph graph)
        {
            string vecPath = Path.Combine(_outPath, "vec");
            Directory.CreateDirectory(vecPath);
            string jsonPath = Path.Combine(vecPath, "graph.json");
            var exportData = new
            {
                nodes = graph.Nodes.Values.Select(n => new { id = n.Id, file = Path.GetFileName(n.FilePath), @namespace = n.Namespace }),
                links = graph.Edges.Select(e => new { source = e.Source, target = e.Target, type = e.Type, details = e.Details })
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonString = JsonSerializer.Serialize(exportData, options);
            File.WriteAllText(jsonPath, jsonString);
        }

        private void ExportMarkdownNotes(CodeGraph graph)
        {
            foreach (var node in graph.Nodes.Values)
            {
                string notePath = Path.Combine(_outPath, $"{node.Id}.md");
                bool isUi = node.Namespace == "SboxGeneratedRazorSpace" || node.FilePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);
                string typeLabel = isUi ? "razor_component" : "class";

                // --- БЕЗПЕЧНА ВСТАВКА ДЛЯ ШІ ---
                string cleanNamespace = string.IsNullOrEmpty(node.Namespace) || node.Namespace == "<global namespace>"
                    ? "global"
                    : node.Namespace.Replace("<", "").Replace(">", "").Trim();

                _chunksToIndex.Add(new DocumentItem
                {
                    id = $"C:{node.Id}",
                    fqn = node.Id,
                    type = typeLabel,
                    text = $"{typeLabel.ToUpper()}: {node.Id}. Namespace: {cleanNamespace}. Source file: {Path.GetFileName(node.FilePath)}."
                });
                // ------------------------------

                using (var writer = new StreamWriter(notePath))
                {
                    writer.WriteLine("---");
                    writer.WriteLine($"type: {typeLabel}");
                    writer.WriteLine($"namespace: {cleanNamespace}");
                    writer.WriteLine("tags:");
                    writer.WriteLine(isUi ? "  - user/ui" : "  - user/logic");
                    writer.WriteLine("---");
                    writer.WriteLine();

                    writer.WriteLine($"# {node.Id}");
                    writer.WriteLine();
                    writer.WriteLine($"**Namespace:** `{cleanNamespace}`  ");
                    writer.WriteLine($"**Source:** `{Path.GetFileName(node.FilePath)}`  ");
                    writer.WriteLine();
                    writer.WriteLine("---");
                    writer.WriteLine();

                    writer.WriteLine("## Out");
                    writer.WriteLine();
                    // Беремо ТІЛЬКИ твої власні зв'язки (без префіксу Engine_)
                    var outgoing = graph.Edges.Where(e => e.Source == node.Id && !e.Type.StartsWith("Engine_")).ToList();
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

                    // --- СЕКЦІЯ ДВИГУНА ТЕПЕР У САМОМУ НИЗУ ФАЙЛУ ---
                    if (Filtering.TypeFilter.IncludeEngineLinks)
                    {
                        writer.WriteLine();
                        writer.WriteLine("## Engine API Dependencies");
                        writer.WriteLine();
                        var engineDeps = graph.Edges.Where(e => e.Source == node.Id && e.Type.StartsWith("Engine_")).ToList();
                        if (engineDeps.Count > 0)
                        {
                            foreach (var edge in engineDeps)
                            {
                                string cleanType = edge.Type.Replace("Engine_", "");
                                writer.WriteLine($"- ─[{cleanType}]─> [[{edge.Target}]]: `{edge.Details}`");
                            }
                        }
                        else
                        {
                            writer.WriteLine("*None*");
                        }
                    }
                }
            }
        }

        private void ExportObsidianCanvas(CodeGraph graph)
        {
            string canvasPath = Path.Combine(_outPath, "graph.canvas");
            int width = 400;
            int height = 220;
            int spacingX = 250;
            int spacingY = 180;

            int cols = (int)Math.Ceiling(Math.Sqrt(graph.Nodes.Count));
            if (cols < 2) cols = 2;

            string folderName = Path.GetFileName(_outPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            var canvasNodes = new List<object>();
            var canvasEdges = new List<object>();
            var nodesList = graph.Nodes.Values.ToList();

            for (int i = 0; i < nodesList.Count; i++)
            {
                var node = nodesList[i];
                int row = i / cols;
                int col = i % cols;

                int x = col * (width + spacingX);
                int y = row * (height + spacingY);

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

            var canvasData = new { nodes = canvasNodes, edges = canvasEdges };
            var options = new JsonSerializerOptions { WriteIndented = true };
            string canvasString = JsonSerializer.Serialize(canvasData, options);
            File.WriteAllText(canvasPath, canvasString);
        }

        // ==========================================
        // СИСТЕМА 2: ЕКСПОРТ ДВИГУНА (Engine API)
        // ==========================================

        public void ExportEngineApi(CodeGraph graph, EngineAnalyzer analyzer)
        {
            Console.WriteLine("Starting smart Engine API export (English, flat file structure)...");
            var registry = analyzer.Registry;

            // 1. Експортуємо JSON та Obsidian Canvas для всього API двигуна
            ExportJson(graph);
            ExportObsidianCanvas(graph);

            // 2. Генеруємо індивідуальні картки для кожного типу двигуна
            foreach (var node in graph.Nodes.Values)
            {
                string notePath = Path.Combine(_outPath, $"{node.Id}.md");

                // Знаходимо опис типу за його ID
                var richType = registry.Values.FirstOrDefault(t =>
                    string.Equals(EngineAnalyzer.GetUniqueId(t.FullName), node.Id, StringComparison.OrdinalIgnoreCase));

                // --- БЕЗПЕЧНА ВСТАВКА ДЛЯ ШІ ---
                if (richType != null)
                {
                    string fqn = richType.FullName;
                    string summaryText = string.IsNullOrEmpty(richType.Summary) ? "No description." : richType.Summary.Trim();

                    // А. Додаємо сам Клас/Енум
                    _chunksToIndex.Add(new DocumentItem
                    {
                        id = $"C:{node.Id}",
                        fqn = fqn,
                        type = richType.IsEnum ? "enum" : "class",
                        text = $"{(richType.IsEnum ? "ENUM" : "CLASS")}: {fqn}. Summary: {summaryText}"
                    });

                    // Б. Додаємо методи класу
                    foreach (var method in richType.Methods.Values)
                    {
                        if (method.IsPublic)
                        {
                            string methodSummary = string.IsNullOrEmpty(method.Summary) ? "No description." : method.Summary.Trim();
                            _chunksToIndex.Add(new DocumentItem
                            {
                                id = method.DocId,
                                fqn = $"{fqn}.{method.Name}",
                                type = "method",
                                text = $"METHOD in {fqn}: {method.Name}. Summary: {methodSummary}"
                            });
                        }
                    }

                    // В. Додаємо властивості класу
                    foreach (var prop in richType.Properties.Values)
                    {
                        if (prop.IsPublic)
                        {
                            string propSummary = string.IsNullOrEmpty(prop.Summary) ? "No description." : prop.Summary.Trim();
                            _chunksToIndex.Add(new DocumentItem
                            {
                                id = prop.DocId,
                                fqn = $"{fqn}.{prop.Name}",
                                type = "property",
                                text = $"PROPERTY in {fqn}: {prop.Name} ({prop.PropertyType}). Summary: {propSummary}"
                            });
                        }
                    }
                }
                // ------------------------------

                using (var writer = new StreamWriter(notePath))
                {
                    if (richType != null)
                    {
                        writer.WriteLine("---");
                        writer.WriteLine($"type: {(richType.IsEnum ? "engine_enum" : "engine_class")}");
                        writer.WriteLine($"namespace: {richType.Namespace}");
                        if (!string.IsNullOrEmpty(richType.BaseType))
                        {
                            if (registry.TryGetValue(richType.BaseType, out var bType))
                            {
                                string parentUniqueId = EngineAnalyzer.GetUniqueId(bType.FullName);
                                writer.WriteLine($"base_type: \"[[{parentUniqueId}]]\"");
                            }
                            else
                            {
                                writer.WriteLine($"base_type: \"{richType.BaseType}\"");
                            }
                        }
                        writer.WriteLine("tags:");
                        writer.WriteLine(richType.IsEnum ? "  - engine/enum" : "  - engine/class");
                        writer.WriteLine("---");
                        writer.WriteLine();
                    }

                    writer.WriteLine($"# {node.Id}");
                    writer.WriteLine();
                    writer.WriteLine($"**Namespace:** `{node.Namespace}`  ");
                    writer.WriteLine($"**Source:** `S&box Engine API`  ");
                    if (richType != null && !string.IsNullOrEmpty(richType.BaseType))
                    {
                        if (registry.TryGetValue(richType.BaseType, out var bType))
                        {
                            string parentUniqueId = EngineAnalyzer.GetUniqueId(bType.FullName);
                            writer.WriteLine($"**Base Type:** [[{parentUniqueId}]]  ");
                        }
                        else
                        {
                            writer.WriteLine($"**Base Type:** `{richType.BaseType}`  ");
                        }
                    }
                    writer.WriteLine();

                    if (richType != null && !string.IsNullOrEmpty(richType.Summary))
                    {
                        writer.WriteLine($"> {richType.Summary.Trim().Replace("\n", "\n> ")}");
                        writer.WriteLine();
                    }

                    writer.WriteLine("---");
                    writer.WriteLine();

                    if (richType != null)
                    {
                        // А. Енуми (Enums)
                        if (richType.IsEnum)
                        {
                            writer.WriteLine("## Fields (Values)");
                            foreach (var field in richType.Fields.Values.Where(f => f.Name != "value__"))
                            {
                                writer.WriteLine($"- **{field.Name}**");
                            }
                            writer.WriteLine();
                        }
                        else
                        {
                            // Б. Поля (Fields)
                            var publicFields = richType.Fields.Values.Where(f => f.IsPublic).ToList();
                            if (publicFields.Count > 0)
                            {
                                writer.WriteLine("## Fields");
                                writer.WriteLine("| Type | Name | Summary |");
                                writer.WriteLine("| --- | --- | --- |");
                                foreach (var field in publicFields)
                                {
                                    string summary = SanitizeSummaryForTable(field.Summary);
                                    string typeLink = FormatTypeWithLinks(field.FieldType, registry);
                                    writer.WriteLine($"| {typeLink} | **{field.Name}** | {summary} |");
                                }
                                writer.WriteLine();
                            }

                            // В. Властивості (Properties)
                            var publicProps = richType.Properties.Values.Where(p => p.IsPublic).ToList();
                            if (publicProps.Count > 0)
                            {
                                writer.WriteLine("## Properties");
                                writer.WriteLine("| Type | Name | Summary |");
                                writer.WriteLine("| --- | --- | --- |");
                                foreach (var prop in publicProps)
                                {
                                    string summary = SanitizeSummaryForTable(prop.Summary);
                                    string typeLink = FormatTypeWithLinks(prop.PropertyType, registry);
                                    writer.WriteLine($"| {typeLink} | **{prop.Name}** | {summary} |");
                                }
                                writer.WriteLine();
                            }

                            // Г. Методи (Methods)
                            var publicMethods = richType.Methods.Values.Where(m => m.IsPublic).ToList();
                            if (publicMethods.Count > 0)
                            {
                                writer.WriteLine("## Methods");
                                writer.WriteLine("| Signature | Summary |");
                                writer.WriteLine("| --- | --- |");
                                foreach (var method in publicMethods)
                                {
                                    string summary = SanitizeSummaryForTable(method.Summary);
                                    string @static = method.IsStatic ? "static " : "";
                                    string returnTypeLink = FormatTypeWithLinks(method.ReturnType, registry);
                                    string @params = string.Join(", ", method.Parameters.Select(p => $"{FormatTypeWithLinks(p.ParameterType, registry)} {p.Name}"));
                                    string signature = $"{@static}{returnTypeLink} {method.Name}({@params})";

                                    writer.WriteLine($"| `{signature}` | {summary} |");
                                }
                                writer.WriteLine();
                            }
                        }
                    }

                    // Д. Зв'язки в нашому стилі "In" / "Out" (Лаконічно та англійською)
                    writer.WriteLine("## Dependencies");
                    writer.WriteLine();

                    // Out (Вихідні зв'язки)
                    writer.WriteLine("### Out");
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

                    // In (Вхідні зв'язки)
                    writer.WriteLine("### In");
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

                    // ДИНАМІЧНІ НАЩАДКИ (Замість окремих великих каталогів)
                    if (richType != null && analyzer.DescendantCounts.TryGetValue(richType.FullName, out int count) && count > 0)
                    {
                        writer.WriteLine();
                        writer.WriteLine("## Derivatives");
                        writer.WriteLine();
                        writer.WriteLine($"Classes inheriting from [[{node.Id}]]:");
                        writer.WriteLine();

                        var children = registry.Values
                            .Where(t => InheritsFrom(t, richType.FullName, registry))
                            .OrderBy(t => t.FullName);

                        foreach (var child in children)
                        {
                            string childUniqueId = EngineAnalyzer.GetUniqueId(child.FullName);
                            writer.WriteLine($"- [[{childUniqueId}]]");
                        }
                    }
                }
            }

            // 3. ГЕНЕРАЦІЯ АВТОМАТИЧНИХ КАТАЛОГІВ
            ExportEnumsCatalog(registry);
            ExportAttributesCatalog(registry);
            ExportHomeIndex(analyzer.LargeFamilies, analyzer.DescendantCounts); // <- Передаємо лічильник   

            Console.WriteLine($"[OK] Dynamic Engine API documentation saved to: {_outPath}");
        }

        private void ExportEnumsCatalog(Dictionary<string, ApiTypeNode> registry)
        {
            string path = Path.Combine(_outPath, "Enums_Catalog.md");
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("# Enums Catalog");
                writer.WriteLine();
                writer.WriteLine("All enumerations defined in the S&box engine API.");
                writer.WriteLine();
                writer.WriteLine("| Enum | Namespace | Summary |");
                writer.WriteLine("| --- | --- | --- |");

                foreach (var item in registry.Values.Where(t => t.IsEnum).OrderBy(t => t.FullName))
                {
                    string uniqueId = EngineAnalyzer.GetUniqueId(item.FullName);
                    string summary = SanitizeSummaryForTable(item.Summary);
                    writer.WriteLine($"| [[{uniqueId}]] | `{item.Namespace}` | {summary} |");
                }
            }
        }

        private void ExportAttributesCatalog(Dictionary<string, ApiTypeNode> registry)
        {
            string path = Path.Combine(_outPath, "Attributes_Catalog.md");
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("# Attributes Catalog");
                writer.WriteLine();
                writer.WriteLine("All decorator attributes `[]` defined in the S&box engine API.");
                writer.WriteLine();
                writer.WriteLine("| Attribute | Namespace | Summary |");
                writer.WriteLine("| --- | --- | --- |");

                foreach (var item in registry.Values.Where(t => t.IsAttribute).OrderBy(t => t.FullName))
                {
                    string uniqueId = EngineAnalyzer.GetUniqueId(item.FullName);
                    string summary = SanitizeSummaryForTable(item.Summary);
                    writer.WriteLine($"| [[{uniqueId}]] | `{item.Namespace}` | {summary} |");
                }
            }
        }

        private void ExportLargeFamilyCatalogs(Dictionary<string, ApiTypeNode> registry, HashSet<string> largeFamilies)
        {
            foreach (var family in largeFamilies)
            {
                string cleanFamilyName = EngineAnalyzer.GetUniqueId(family);
                string path = Path.Combine(_outPath, $"{cleanFamilyName}_Catalog.md");

                using (var writer = new StreamWriter(path))
                {
                    writer.WriteLine($"# {family} Catalog");
                    writer.WriteLine();
                    writer.WriteLine($"All classes inheriting from [[{cleanFamilyName}]].");
                    writer.WriteLine();
                    writer.WriteLine("| Type | Namespace | Summary |");
                    writer.WriteLine("| --- | --- | --- |");

                    var children = registry.Values
                        .Where(t => InheritsFrom(t, family, registry))
                        .OrderBy(t => t.FullName);

                    foreach (var child in children)
                    {
                        string uniqueId = EngineAnalyzer.GetUniqueId(child.FullName);
                        string summary = string.IsNullOrEmpty(child.Summary) ? "-" : child.Summary.Replace("\r", "").Replace("\n", " ");
                        writer.WriteLine($"| [[{uniqueId}]] | `{child.Namespace}` | {summary} |");
                    }
                }
            }
        }

        private void ExportHomeIndex(HashSet<string> largeFamilies, Dictionary<string, int> descendantCounts)
        {
            string path = Path.Combine(_outPath, "Home.md");
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("# S&box API Catalog Index");
                writer.WriteLine();
                writer.WriteLine("Welcome to the dynamically generated S&box engine API documentation.");
                writer.WriteLine();
                writer.WriteLine("## System Catalogs");
                writer.WriteLine("- [[Enums_Catalog]] — All enumerations");
                writer.WriteLine("- [[Attributes_Catalog]] — All decorator attributes");
                writer.WriteLine();
                writer.WriteLine("## Major Class Hierarchies");
                writer.WriteLine();

                // Сортуємо родини від найбільшої (найвища кількість нащадків) до найменшої
                var sortedFamilies = largeFamilies
                    .OrderByDescending(f => descendantCounts.TryGetValue(f, out int c) ? c : 0);

                foreach (var family in sortedFamilies)
                {
                    string uniqueId = EngineAnalyzer.GetUniqueId(family);
                    int count = descendantCounts.TryGetValue(family, out int c) ? c : 0;
                    writer.WriteLine($"- [[{uniqueId}]] ({count} classes)");
                }
            }
        }

        // ==========================================
        // ДОПОМІЖНІ МЕТОДИ
        // ==========================================

        private string FormatTypeWithLinks(string? typeStr, Dictionary<string, ApiTypeNode> registry)
        {
            if (string.IsNullOrEmpty(typeStr)) return "void";
            var signature = TypeResolver.Parse(typeStr);
            return FormatSignatureWithLinks(signature, registry);
        }

        private string FormatSignatureWithLinks(TypeSignature signature, Dictionary<string, ApiTypeNode> registry)
        {
            if (signature == null) return "object";

            string name = signature.FullName;

            if (registry.TryGetValue(name, out var target))
            {
                string uniqueId = EngineAnalyzer.GetUniqueId(target.FullName);
                name = $"[[{uniqueId}]]";
            }
            else
            {
                var byShort = registry.Values.FirstOrDefault(t => string.Equals(t.Name, signature.FullName, StringComparison.OrdinalIgnoreCase));
                if (byShort != null)
                {
                    string uniqueId = EngineAnalyzer.GetUniqueId(byShort.FullName);
                    name = $"[[{uniqueId}]]";
                }
                else
                {
                    name = signature.CleanName;
                }
            }

            if (signature.GenericArguments.Count > 0)
            {
                var args = signature.GenericArguments.Select(arg => FormatSignatureWithLinks(arg, registry));
                name += $"<{string.Join(", ", args)}>";
            }

            if (signature.IsArray) name += "[]";
            return name;
        }

        private bool InheritsFrom(ApiTypeNode type, string targetBase, Dictionary<string, ApiTypeNode> registry)
        {
            string? current = type.BaseType;
            int depth = 0;

            while (!string.IsNullOrEmpty(current) && depth < 25)
            {
                if (string.Equals(current, targetBase, StringComparison.OrdinalIgnoreCase)) return true;
                if (registry.TryGetValue(current, out var baseNode))
                {
                    current = baseNode.BaseType;
                }
                else
                {
                    var byShort = registry.Values.FirstOrDefault(t => string.Equals(t.Name, current, StringComparison.OrdinalIgnoreCase));
                    if (byShort != null)
                    {
                        current = byShort.BaseType;
                    }
                    else
                    {
                        break;
                    }
                }
                depth++;
            }
            return false;
        }

        private string SanitizeSummaryForTable(string? summary)
        {
            if (string.IsNullOrEmpty(summary)) return "-";
            return summary
                .Replace("\r", "")
                .Replace("\n", " ")
                .Replace("|", "&#124;") // Надійний захист від пошкодження колонок Markdown
                .Trim();
        }


        /// <summary>
        /// Фоновий асинхронний запуск індексації зібраних чанків на Python-сервісі.
        /// </summary>
        public async Task TriggerSemanticIndexingAsync(string projectId)
        {
            if (_chunksToIndex.Count == 0) return;

            Console.WriteLine($"\n[C#] Підготовка до фонової індексації {_chunksToIndex.Count} чанків...");

            // Запускаємо відправку в окремому асинхронному потоці, щоб не блокувати головний потік C#
            await Task.Run(async () =>
            {
                await _librarianClient.IndexProjectAsync(projectId, _outPath, _chunksToIndex);
            });
        }

    }

}