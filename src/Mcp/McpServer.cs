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
                new {
                    name = "sbox_user_explain_component",
                    description = "Get structure, incoming, outgoing or engine dependencies for a custom user class.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            class_name = new { type = "string", description = "Target class name (e.g. SwarmManager)" },
                            view_mode = new { type = "string", description = "Options: 'all', 'in', 'out', 'engine_deps', 'summary'", @default = "all" }
                        },
                        required = new[] { "class_name" }
                    }
                },
                new {
                    name = "sbox_user_find_path",
                    description = "Find shortest architecture path between two classes using BFS.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            from_class = new { type = "string" },
                            to_class = new { type = "string" },
                            undirected = new { type = "boolean", @default = true }
                        },
                        required = new[] { "from_class", "to_class" }
                    }
                },
                new {
                    name = "sbox_user_check_cycles",
                    description = "Detect cyclic dependency loops in project code.",
                    inputSchema = new { type = "object", properties = new { } }
                },
                new {
                    name = "sbox_user_get_metrics",
                    description = "Get project metrics: Hubs, God Nodes, and isolated code.",
                    inputSchema = new { type = "object", properties = new { } }
                },
                new {
                    name = "sbox_user_semantic_search",
                    description = "RAG vector search over project documentation & code chunks using Librarian AI.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            query = new { type = "string", description = "Natural language prompt or query" }
                        },
                        required = new[] { "query" }
                    }
                },
                new {
                    name = "sbox_user_read_source_code",
                    description = "Reads the actual .cs or .razor file content for a given class.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            class_name = new { type = "string" }
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
                    case "sbox_user_explain_component":
                        string cls = args.GetProperty("class_name").GetString()!;
                        string mode = args.TryGetProperty("view_mode", out var m) ? m.GetString() ?? "all" : "all";
                        return _queryEngine.Explain(cls, mode);

                    case "sbox_user_find_path":
                        string from = args.GetProperty("from_class").GetString()!;
                        string to = args.GetProperty("to_class").GetString()!;
                        bool undir = args.TryGetProperty("undirected", out var u) ? u.GetBoolean() : true;
                        return _queryEngine.FindPath(from, to, undir);

                    case "sbox_user_check_cycles":
                        return _queryEngine.FindCycles();

                    case "sbox_user_get_metrics":
                        return _queryEngine.GetMetrics();

                    case "sbox_user_semantic_search":
                        string q = args.GetProperty("query").GetString()!;
                        var res = await _librarianClient.QuerySemanticAsync(_outDir, q);
                        if (res == null || res.matches.Count == 0) return "No semantic matches found.";
                        return string.Join("\n\n", res.matches.Select(match => $"[{match.type.ToUpper()}] {match.fqn} (Score: {match.score:F2})\n{match.preview}"));

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
    }
}