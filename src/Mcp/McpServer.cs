using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SboxAstGraph.Analysis;

namespace SboxAstGraph.Workspace
{
    public class McpServer
    {
        private readonly QueryEngine _queryEngine;
        private readonly LibrarianClient _librarianClient;
        private readonly string _outDir;
        private readonly string _srcDir;
        private readonly TextWriter _mcpStdout;

        public McpServer(QueryEngine queryEngine, LibrarianClient librarianClient, string outDir, string srcDir, TextWriter mcpStdout)
        {
            _queryEngine = queryEngine;
            _librarianClient = librarianClient;
            _outDir = outDir;
            _srcDir = srcDir;
            _mcpStdout = mcpStdout;
        }

        public async Task ListenAsync()
        {
            Console.Error.WriteLine("[MCP Server] SboxAstGraph MCP Active. Listening on stdio...");

            while (true)
            {
                string? line = await Console.In.ReadLineAsync();
                if (line == null) break;

                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    string jsonrpc = root.GetProperty("jsonrpc").GetString() ?? "2.0";

                    if (!root.TryGetProperty("id", out var idElem))
                    {
                        // Це notification (наприклад notifications/initialized), просто ігноруємо або обробляємо
                        continue;
                    }

                    long id = idElem.GetInt64();
                    string method = root.GetProperty("method").GetString() ?? "";

                    var paramsElem = root.TryGetProperty("params", out var p) ? p : default;

                    await HandleMethodAsync(id, method, paramsElem);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[MCP Error] Failed to parse input: {ex.Message}");
                }
            }
        }

        private async Task HandleMethodAsync(long id, string method, JsonElement paramsElem)
        {
            switch (method)
            {
                case "initialize":
                    SendResponse(id, new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new { tools = new { } },
                        serverInfo = new { name = "SboxAstGraph MCP", version = "1.0.0" }
                    });
                    break;

                case "tools/list":
                    SendResponse(id, new { tools = GetToolsList() });
                    break;

                case "tools/call":
                    string toolName = paramsElem.GetProperty("name").GetString() ?? "";
                    var args = paramsElem.GetProperty("arguments");
                    var resultText = await ExecuteToolAsync(toolName, args);

                    SendResponse(id, new
                    {
                        content = new[]
                        {
                            new { type = "text", text = resultText }
                        }
                    });
                    break;

                default:
                    SendError(id, -32601, $"Method '{method}' not found");
                    break;
            }
        }

        private object[] GetToolsList()
        {
            return new object[]
            {
                // ==================== ENGINE API TOOLS ====================
                new {
                    name = "sbox_engine_search_api",
                    description = "Search the official S&box Engine API for types, methods, properties, or engine features using hybrid, keyword, or semantic search.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            query = new { type = "string", description = "Search term, API name, or feature concept to locate in the S&box Engine API." },
                            search_mode = new { type = "string", description = "Search strategy: 'hybrid' (combined exact + semantic), 'keyword' (exact name match), or 'semantic' (concept match).", @default = "hybrid" },
                            member_type = new { type = "string", description = "Filter search hits by API entity kind: 'all', 'class', 'method', 'property', 'enum', or 'struct'.", @default = "all" },
                            max_results = new { type = "integer", description = "Maximum number of search hits to return per section (default 5).", @default = 5 }
                        },
                        required = new[] { "query" }
                    }
                },
                new {
                    name = "sbox_engine_explain",
                    description = "Retrieve comprehensive API documentation for a specific S&box Engine type, including member signatures, summaries, and code usage dependencies.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            class_name = new { type = "string", description = "Target S&box Engine class, struct, interface, or enum name (short or fully qualified)." },
                            view_mode = new { type = "string", description = "Documentation detail level: 'all' (full doc), 'summary' (overview), 'methods' (methods list), 'properties' (properties & fields), or 'dependencies' (usage graph).", @default = "all" }
                        },
                        required = new[] { "class_name" }
                    }
                },

                // ==================== USER CODE TOOLS ====================
                new {
                    name = "sbox_user_semantic_search",
                    description = "RAG semantic search over user code base.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            query = new { type = "string", description = "Short natural language prompt or technical query" },
                            max_results = new { type = "integer", description = "Number of top matching rows to return (default 5)", @default = 5 }
                        },
                        required = new[] { "query" }
                    }
                },
                new {
                    name = "sbox_user_explain_class",
                    description = "Get class architecture: incoming/outgoing user code dependencies and engine API usages.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            class_name = new { type = "string", description = "Target class name" },
                            view_mode = new { type = "string", description = "Options: 'in', 'out', 'in_out' (all user code), 'engine', 'all'", @default = "all" }
                        },
                        required = new[] { "class_name" }
                    }
                }
            };
        }

        private async Task<string> ExecuteToolAsync(string toolName, JsonElement args)
        {
            try
            {
                switch (toolName)
                {
                    case "sbox_user_explain_class":
                    case "sbox_user_explain_component":
                        string cls = args.GetProperty("class_name").GetString()!;
                        string mode = args.TryGetProperty("view_mode", out var m) ? m.GetString() ?? "all" : "all";
                        return _queryEngine.Explain(cls, mode);

                    case "sbox_user_find_path":
                        string from = args.GetProperty("from_class").GetString()!;
                        string to = args.GetProperty("to_class").GetString()!;
                        bool undir = args.TryGetProperty("undirected", out var u) ? u.GetBoolean() : true;
                        return _queryEngine.FindPath(from, to, undir);

                    case "sbox_engine_explain":
                    case "sbox_engine_explain_type":
                        string targetEngineCls = args.GetProperty("class_name").GetString()!;
                        string engineViewMode = args.TryGetProperty("view_mode", out var evm) ? evm.GetString() ?? "all" : "all";
                        return ExplainEngineType(targetEngineCls, engineViewMode);

                    case "sbox_user_check_cycles":
                        return _queryEngine.FindCycles();

                    case "sbox_user_get_metrics":
                        return _queryEngine.GetMetrics();

                    case "sbox_user_semantic_search":
                        string q = args.TryGetProperty("query", out var qElem) ? qElem.GetString() ?? "" : "";
                        int maxResults = args.TryGetProperty("max_results", out var mr) ? mr.GetInt32() : 5;

                        if (string.IsNullOrWhiteSpace(q)) return "Error: Query parameter is empty.";

                        string userIndexDir = Path.Combine(_outDir, "user_code");
                        if (!Directory.Exists(userIndexDir)) userIndexDir = _outDir;

                        var res = await _librarianClient.QuerySemanticAsync(userIndexDir, q, maxResults);

                        if (res == null || res.matches == null || res.matches.Count == 0)
                        {
                            return $"No semantic matches found for query: '{q}' in User Code index.";
                        }

                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"## Semantic Search (User Code): `{q}`\n");

                        int index = 1;
                        foreach (var match in res.matches)
                        {
                            if (match == null) continue;

                            string fqn = match.fqn ?? "Unknown";
                            string type = match.type ?? "class";
                            double score = match.score * 100.0;
                            string preview = match.preview ?? "";

                            string ns = "global";
                            string file = $"{fqn}.cs";

                            // Safe extraction of Namespace
                            if (preview.Contains("Namespace:"))
                            {
                                int nsIdx = preview.IndexOf("Namespace:");
                                if (nsIdx != -1)
                                {
                                    string afterNs = preview.Substring(nsIdx + 10);
                                    int endIdx = afterNs.IndexOf('.');
                                    if (endIdx != -1) ns = afterNs.Substring(0, endIdx).Trim();
                                }
                            }

                            // Safe extraction of Source file (preserving .cs / .razor without trailing dots)
                            if (preview.Contains("Source file:"))
                            {
                                int fileIdx = preview.IndexOf("Source file:");
                                if (fileIdx != -1)
                                {
                                    string afterFile = preview.Substring(fileIdx + 12).Trim();
                                    afterFile = afterFile.TrimEnd('.', ' ');
                                    if (!string.IsNullOrEmpty(afterFile)) file = afterFile;
                                }
                            }

                            sb.AppendLine($"{index++}. ({score:F1}%) [[{fqn}]] (`{type}`) | `{ns}` | `{file}`");
                        }

                        return sb.ToString().TrimEnd();

                    case "sbox_engine_search_api":
                        string queryStr = args.GetProperty("query").GetString()!;
                        string searchMode = args.TryGetProperty("search_mode", out var sm) ? sm.GetString() ?? "hybrid" : "hybrid";
                        string memberType = args.TryGetProperty("member_type", out var mt) ? mt.GetString() ?? "all" : "all";
                        int limit = args.TryGetProperty("max_results", out var lim) ? lim.GetInt32() : 5;

                        searchMode = searchMode.ToLower().Trim();
                        var resultSb = new System.Text.StringBuilder();

                        // 1. Блискавичний Keyword search у C#
                        if (searchMode is "keyword" or "hybrid")
                        {
                            string kwResults = _queryEngine.SearchEngineKeyword(queryStr, limit, memberType);
                            if (searchMode == "hybrid") resultSb.AppendLine("## Keyword Hits");
                            resultSb.AppendLine(kwResults);
                            if (searchMode == "hybrid") resultSb.AppendLine();
                        }

                        // 2. Semantic search із завантаженням та фільтрацією по member_type
                        if (searchMode is "semantic" or "hybrid")
                        {
                            try
                            {
                                string engineDir = ResolveIndexDir(Path.Combine(Directory.GetParent(_outDir)?.FullName ?? _outDir, "engine_library"));
                                if (!Directory.Exists(engineDir)) engineDir = ResolveIndexDir(_outDir);

                                // Динамічний розмір пулу кандидатів: для "all" - точний limit, для окремих типів - пул limit * 10
                                int fetchLimit = memberType.Equals("all", StringComparison.OrdinalIgnoreCase)
                                    ? limit
                                    : Math.Clamp(limit * 10, 20, 100);

                                var semTask = _librarianClient.QuerySemanticAsync(engineDir, queryStr, fetchLimit);

                                if (await Task.WhenAny(semTask, Task.Delay(35000)) == semTask)
                                {
                                    var semRes = await semTask;
                                    if (searchMode == "hybrid") resultSb.AppendLine("## Semantic Hits");

                                    if (semRes != null && semRes.matches != null && semRes.matches.Count > 0)
                                    {
                                        int idx = 1;
                                        int count = 0;
                                        foreach (var match in semRes.matches)
                                        {
                                            if (match == null) continue;

                                            // Фільтрація за member_type
                                            if (!IsMemberTypeMatch(match.type, memberType, match.fqn ?? "")) continue;

                                            resultSb.AppendLine(FormatEngineMatch(match, idx++));
                                            count++;
                                            if (count >= limit) break;
                                        }

                                        if (count == 0)
                                        {
                                            resultSb.AppendLine($"*No semantic matches found for type '{memberType}'.*");
                                        }
                                    }
                                    else
                                    {
                                        resultSb.AppendLine("*No semantic matches found.*");
                                    }
                                }
                                else
                                {
                                    if (searchMode == "hybrid") resultSb.AppendLine("## Semantic Hits");
                                    resultSb.AppendLine("*Semantic index loading timed out.*");
                                }
                            }
                            catch
                            {
                                if (searchMode == "hybrid") resultSb.AppendLine("## Semantic Hits");
                                resultSb.AppendLine("*Semantic search unavailable.*");
                            }
                        }

                        return resultSb.ToString().TrimEnd();

                    case "sbox_user_read_source_code":
                        string targetCls = args.GetProperty("class_name").GetString()!;
                        string searchResult = _queryEngine.Search(targetCls);
                        // Якщо знайшли у результатах шлях до файлу — шукаємо та читаємо
                        var files = Directory.GetFiles(_srcDir, $"{targetCls}.*", SearchOption.AllDirectories);
                        if (files.Length > 0)
                        {
                            return $"--- FILE: {files[0]} ---\n" + File.ReadAllText(files[0]);
                        }
                        return $"Source file for '{targetCls}' was not found directly on disk.";

                    default:
                        return $"Unknown tool: {toolName}";
                }
            }
            catch (Exception ex)
            {
                return $"Error executing tool '{toolName}': {ex.Message}";
            }
        }

        private void SendResponse(long id, object result)
        {
            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                result = result
            };
            string json = JsonSerializer.Serialize(response);
            _mcpStdout.WriteLine(json);
            _mcpStdout.Flush();
        }

        private void SendError(long id, int code, string message)
        {
            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                error = new { code = code, message = message }
            };
            string json = JsonSerializer.Serialize(response);
            _mcpStdout.WriteLine(json);
            _mcpStdout.Flush();
        }

        private string FormatEngineMatch(QueryResponseMatch match, int index)
        {
            double score = match.score * 100.0;
            string typeUpper = string.IsNullOrWhiteSpace(match.type) ? "[TYPE]" : $"[{match.type.ToUpper()}]";
            string fqn = match.fqn ?? "Unknown";
            string ns = ExtractNamespace(fqn);

            // Пробуємо отримати точну сигнатуру з реєстра API двигуна
            string signatureInfo = _queryEngine.GetEngineMemberSignature(match.id, fqn, match.type ?? "");

            // Якщо не знайшли в реєстрі — пробуємо витягнути з preview
            if (string.IsNullOrEmpty(signatureInfo))
            {
                string rawSig = ExtractSignatureFromPreview(match.preview);
                if (!string.IsNullOrEmpty(rawSig)) signatureInfo = $": `{rawSig}`";
            }

            string? summary = ExtractCleanSummary(match.preview);

            var sb = new System.Text.StringBuilder();
            sb.Append($"{index}. ({score:F1}%) {typeUpper} [[{fqn}]]");

            if (!string.IsNullOrEmpty(signatureInfo))
            {
                sb.Append($" {signatureInfo}");
            }

            sb.Append($" | `{ns}`");

            if (!string.IsNullOrEmpty(summary))
            {
                sb.AppendLine();
                sb.Append($"   > {summary}");
            }

            return sb.ToString();
        }

        private static string ExtractNamespace(string fqn)
        {
            if (string.IsNullOrEmpty(fqn)) return "global";
            int lastDot = fqn.LastIndexOf('.');
            if (lastDot <= 0) return "global";

            return fqn.Substring(0, lastDot);
        }

        private static string ExtractSignatureFromPreview(string preview)
        {
            if (string.IsNullOrEmpty(preview)) return "";

            int sumIdx = preview.IndexOf("Summary:");
            string beforeSummary = sumIdx != -1 ? preview.Substring(0, sumIdx) : preview;

            int openParen = beforeSummary.IndexOf('(');
            int closeParen = beforeSummary.LastIndexOf(')');

            if (openParen != -1 && closeParen > openParen)
            {
                string inside = beforeSummary.Substring(openParen + 1, closeParen - openParen - 1).Trim();
                if (!string.IsNullOrEmpty(inside))
                {
                    int lastDot = inside.LastIndexOf('.');
                    if (lastDot != -1 && !inside.Contains(' '))
                    {
                        return inside.Substring(lastDot + 1);
                    }
                    return inside;
                }
            }

            return "";
        }

        private static string? ExtractCleanSummary(string preview)
        {
            if (string.IsNullOrEmpty(preview)) return null;

            int sumIdx = preview.IndexOf("Summary:");
            if (sumIdx == -1) return null;

            string summary = preview.Substring(sumIdx + 8).Trim();

            // Сплющуємо всі переноси рядків та подвійні пробіли
            summary = summary.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
            while (summary.Contains("  ")) summary = summary.Replace("  ", " ");

            summary = summary.TrimEnd('.', ' ');

            if (string.IsNullOrWhiteSpace(summary) ||
                summary.Equals("No description", StringComparison.OrdinalIgnoreCase) ||
                summary.Equals("No description available", StringComparison.OrdinalIgnoreCase) ||
                summary.StartsWith("No description", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return summary;
        }

        private string ResolveIndexDir(string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir)) return baseDir;

            string[] candidates = new[]
            {
                Path.Combine(baseDir, "engine_api", "vec"),
                Path.Combine(baseDir, "engine_api"),
                Path.Combine(baseDir, "user_code", "vec"),
                Path.Combine(baseDir, "user_code"),
                Path.Combine(baseDir, "vec"),
                baseDir
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "semantic_index.tvim")) ||
                    File.Exists(Path.Combine(candidate, "graph.json")))
                {
                    return candidate;
                }
            }

            return baseDir;
        }

        private static bool IsMemberTypeMatch(string? rawType, string targetType, string fqn)
        {
            // Ігноруємо анонімні та службові класи компілятора (<G>$, <M>$, <>c__DisplayClass)
            if (!string.IsNullOrEmpty(fqn) && (fqn.Contains('<') || fqn.Contains('$')))
                return false;

            if (string.IsNullOrEmpty(targetType) || targetType.Equals("all", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.IsNullOrEmpty(rawType)) return false;

            string actual = rawType.ToLower().Trim();
            string expected = targetType.ToLower().Trim();

            if (expected is "class" or "type")
                return actual is "class" or "struct" or "interface" or "enum" or "type";

            if (expected is "property" or "prop")
                return actual is "property" or "prop" or "field";

            if (expected is "method")
                return actual is "method";

            return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        private string ExplainEngineType(string className, string viewMode = "all")
        {
            if (string.IsNullOrWhiteSpace(className)) return "Error: Parameter 'class_name' is required.";

            className = className.Trim();
            viewMode = viewMode.ToLower().Trim();

            // 1. Шукаємо директорію engine_api
            string engineDir = Path.Combine(_outDir, "engine_api");
            if (!Directory.Exists(engineDir))
            {
                engineDir = Directory.GetParent(_outDir)?.FullName ?? _outDir;
                engineDir = Path.Combine(engineDir, "engine_api");
            }

            if (!Directory.Exists(engineDir))
            {
                engineDir = _outDir; // Fallback
            }

            // 2. Пошук відповідного .md файлу на диску
            string targetFile = "";
            if (Directory.Exists(engineDir))
            {
                var files = Directory.GetFiles(engineDir, "*.md", SearchOption.AllDirectories);

                // А. Точний збіг по імені файлу (наприклад, Color.md або Sandbox.Color.md)
                targetFile = files.FirstOrDefault(f =>
                    string.Equals(Path.GetFileNameWithoutExtension(f), className, StringComparison.OrdinalIgnoreCase)) ?? "";

                if (string.IsNullOrEmpty(targetFile))
                {
                    // Б. Пошук файлу, що закінчується на .className (наприклад, Sandbox.Physics.CollisionRules.md за запитом CollisionRules)
                    targetFile = files.FirstOrDefault(f =>
                        Path.GetFileNameWithoutExtension(f).EndsWith("." + className, StringComparison.OrdinalIgnoreCase)) ?? "";
                }

                if (string.IsNullOrEmpty(targetFile))
                {
                    // В. Запасний варіант: файл містить назву класу
                    targetFile = files.FirstOrDefault(f =>
                        Path.GetFileNameWithoutExtension(f).Contains(className, StringComparison.OrdinalIgnoreCase)) ?? "";
                }
            }

            if (string.IsNullOrEmpty(targetFile) || !File.Exists(targetFile))
            {
                return $"[Error] Documentation file for Engine type '{className}' was not found in '{engineDir}'.";
            }

            string fullText = File.ReadAllText(targetFile);

            // 3. Повертаємо увесь текст, якщо viewMode = "all"
            if (viewMode is "all")
            {
                return fullText;
            }

            // 4. Вирізаємо конкретну секцію для розбірливого виводу
            return ExtractMarkdownSection(fullText, viewMode, className);
        }

        private static string ExtractMarkdownSection(string fullText, string viewMode, string className)
        {
            var lines = fullText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var resultSb = new System.Text.StringBuilder();

            if (viewMode is "summary")
            {
                foreach (var line in lines)
                {
                    if (line.StartsWith("## ")) break; // Зупиняємося на першій секції ##
                    resultSb.AppendLine(line);
                }
                return resultSb.ToString().TrimEnd();
            }

            string targetHeader = viewMode switch
            {
                "methods" => "## Methods",
                "properties" or "fields" => "## Properties",
                "dependencies" or "usages" or "deps" => "## Dependencies",
                _ => ""
            };

            if (string.IsNullOrEmpty(targetHeader)) return fullText;

            bool capture = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("## "))
                {
                    if (line.StartsWith(targetHeader, StringComparison.OrdinalIgnoreCase) ||
                        (viewMode is "properties" && line.StartsWith("## Fields", StringComparison.OrdinalIgnoreCase)))
                    {
                        capture = true;
                    }
                    else if (capture)
                    {
                        break; // Зупиняємося, коли почалася наступна секція ##
                    }
                }

                if (capture)
                {
                    resultSb.AppendLine(line);
                }
            }

            if (resultSb.Length == 0)
            {
                return $"*Section '{viewMode}' not found in documentation for '{className}'.*";
            }

            return resultSb.ToString().TrimEnd();
        }
    }
}