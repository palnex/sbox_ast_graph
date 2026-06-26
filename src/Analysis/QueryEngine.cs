using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SboxAstGraph.Analysis
{

    public class QueryEngine
    {
        // Локальні моделі для швидкої десеріалізації кешу graph.json
        private class CacheNode
        {
            public string id { get; set; } = string.Empty;
            public string file { get; set; } = string.Empty;
            public string @namespace { get; set; } = string.Empty;
        }

        private class CacheLink
        {
            public string source { get; set; } = string.Empty;
            public string target { get; set; } = string.Empty;
            public string type { get; set; } = string.Empty;
            public string details { get; set; } = string.Empty;
        }

        private class CacheGraph
        {
            public List<CacheNode> nodes { get; set; } = new();
            public List<CacheLink> links { get; set; } = new();
        }

        private readonly CacheGraph _graph = new();
        private readonly Dictionary<string, List<CacheLink>> _adjacencyList = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<CacheLink>> _undirectedAdjacencyList = new(StringComparer.OrdinalIgnoreCase);

        public QueryEngine(string cacheFilePath)
        {
            if (!File.Exists(cacheFilePath))
            {
                throw new FileNotFoundException($"Файл кешу не знайдено за шляхом: {cacheFilePath}. Спочатку запустіть повний аналіз.");
            }

            string json = File.ReadAllText(cacheFilePath);
            var parsed = JsonSerializer.Deserialize<CacheGraph>(json);
            if (parsed != null)
            {
                _graph = parsed;
                BuildAdjacencyLists();
            }
        }

        private void BuildAdjacencyLists()
        {
            foreach (var node in _graph.nodes)
            {
                _adjacencyList[node.id] = new List<CacheLink>();
                _undirectedAdjacencyList[node.id] = new List<CacheLink>();
            }

            foreach (var link in _graph.links)
            {
                if (_adjacencyList.ContainsKey(link.source))
                {
                    _adjacencyList[link.source].Add(link);
                }

                if (_undirectedAdjacencyList.ContainsKey(link.source))
                {
                    _undirectedAdjacencyList[link.source].Add(link);
                }
                if (_undirectedAdjacencyList.ContainsKey(link.target))
                {
                    _undirectedAdjacencyList[link.target].Add(link);
                }
            }
        }

        /// <summary>
        /// Пошук найкоротшого шляху між двома класами за допомогою BFS
        /// </summary>
        public string FindPath(string from, string to, bool undirected = false)
        {
            var activeAdjList = undirected ? _undirectedAdjacencyList : _adjacencyList;

            if (!activeAdjList.ContainsKey(from)) return $"[Помилка] Початковий клас '{from}' не знайдено в базі.";
            if (!activeAdjList.ContainsKey(to)) return $"[Помилка] Кінцевий клас '{to}' не знайдено в базі.";

            var queue = new Queue<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parent = new Dictionary<string, CacheLink>(StringComparer.OrdinalIgnoreCase);

            queue.Enqueue(from);
            visited.Add(from);

            bool found = false;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (string.Equals(current, to, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }

                if (activeAdjList.TryGetValue(current, out var edges))
                {
                    foreach (var edge in edges)
                    {
                        string neighbor = string.Equals(edge.source, current, StringComparison.OrdinalIgnoreCase)
                            ? edge.target
                            : edge.source;

                        if (!visited.Contains(neighbor))
                        {
                            visited.Add(neighbor);
                            parent[neighbor] = edge;
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            if (!found)
            {
                return $"Зв'язок між '{from}' та '{to}' {(undirected ? "повністю відсутній" : "в цьому напрямку відсутній")}.";
            }

            var path = new List<CacheLink>();
            var pathNodes = new List<string>();
            string curr = to;
            pathNodes.Add(curr);

            while (curr != from)
            {
                if (parent.TryGetValue(curr, out var edge))
                {
                    path.Add(edge);
                    curr = string.Equals(edge.source, curr, StringComparison.OrdinalIgnoreCase) ? edge.target : edge.source;
                    pathNodes.Add(curr);
                }
                else
                {
                    break;
                }
            }

            path.Reverse();
            pathNodes.Reverse();

            var result = new System.Text.StringBuilder();
            result.AppendLine($"Знайдено {(undirected ? "неспрямований" : "спрямований")} зв'язок між {from} та {to} ({path.Count} кроків):");

            for (int i = 0; i < path.Count; i++)
            {
                var step = path[i];
                string currentNode = pathNodes[i];

                if (string.Equals(step.source, currentNode, StringComparison.OrdinalIgnoreCase))
                {
                    result.AppendLine($"{i + 1}. [{step.source}] ──({step.type}: {step.details})──> [{step.target}]");
                }
                else
                {
                    result.AppendLine($"{i + 1}. [{step.target}] <──({step.type}: {step.details})── [{step.source}]");
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Дає стислий текстовий опис класу та його безпосередніх зв'язків
        /// </summary>
        public string Explain(string className)
        {
            var node = _graph.nodes.FirstOrDefault(n => string.Equals(n.id, className, StringComparison.OrdinalIgnoreCase));
            if (node == null)
            {
                return $"[Помилка] Клас '{className}' не знайдено в базі.";
            }

            var outgoing = _graph.links.Where(l => string.Equals(l.source, className, StringComparison.OrdinalIgnoreCase)).ToList();
            var incoming = _graph.links.Where(l => string.Equals(l.target, className, StringComparison.OrdinalIgnoreCase)).ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Клас: {node.id}");
            sb.AppendLine($"Простір імен: {node.@namespace}");
            sb.AppendLine($"Файл: {node.file}");
            sb.AppendLine("------------------------------------");

            sb.AppendLine($"Вихідні зв'язки (Dependencies: {outgoing.Count}):");
            if (outgoing.Count > 0)
            {
                foreach (var edge in outgoing.Take(15))
                {
                    sb.AppendLine($"  -> [{edge.type}] {edge.target} ({edge.details})");
                }
                if (outgoing.Count > 15) sb.AppendLine($"  ... та ще {outgoing.Count - 15} зв'язків.");
            }
            else
            {
                sb.AppendLine("  (Немає вихідних зв'язків)");
            }

            sb.AppendLine();
            sb.AppendLine($"Вхідні зв'язки (Dependents: {incoming.Count}):");
            if (incoming.Count > 0)
            {
                foreach (var edge in incoming.Take(15))
                {
                    sb.AppendLine($"  <- [{edge.type}] {edge.source} ({edge.details})");
                }
                if (incoming.Count > 15) sb.AppendLine($"  ... та ще {incoming.Count - 15} зв'язків.");
            }
            else
            {
                sb.AppendLine("  (Ніхто не посилається на цей клас)");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Фільтрація / пошук за текстовим шаблоном
        /// </summary>
        public string Search(string query)
        {
            var matches = _graph.nodes
                .Where(n => n.id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            n.@namespace.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                return $"За запитом '{query}' збігів не знайдено.";
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Знайдено збігів: {matches.Count}");
            foreach (var match in matches.Take(30))
            {
                sb.AppendLine($"- {match.id} ({match.@namespace}) -> {match.file}");
            }
            if (matches.Count > 30)
            {
                sb.AppendLine($"... та ще {matches.Count - 30} результатів.");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Розраховує архітектурні метрики: God Nodes, Hubs та ізольовані класи.
        /// </summary>
        public string GetMetrics()
        {
            var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var outDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Ініціалізуємо лічильники для всіх вузлів
            foreach (var node in _graph.nodes)
            {
                inDegree[node.id] = 0;
                outDegree[node.id] = 0;
            }

            // Рахуємо зв'язки
            foreach (var link in _graph.links)
            {
                if (outDegree.ContainsKey(link.source)) outDegree[link.source]++;
                if (inDegree.ContainsKey(link.target)) inDegree[link.target]++;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== АРХІТЕКТУРНІ МЕТРИКИ ПРОЄКТУ ===");
            sb.AppendLine($"Всього класів (Nodes): {_graph.nodes.Count}");
            sb.AppendLine($"Всього зв'язків (Edges): {_graph.links.Count}");
            sb.AppendLine("------------------------------------");

            // 1. Пошук "Hubs" (Ядро системи - класи, від яких найбільше залежать)
            sb.AppendLine("Топ-5 найважливіших хабів (найвища вхідна вага):");
            var topHubs = inDegree.OrderByDescending(kvp => kvp.Value).Take(5).ToList();
            foreach (var hub in topHubs)
            {
                sb.AppendLine($"  - [{hub.Key}]: від нього залежать {hub.Value} класів");
            }

            sb.AppendLine();

            // 2. Пошук "God Nodes" (Класи з найбільшою кількістю вихідних зв'язків)
            sb.AppendLine("Топ-5 потенційних 'God Nodes' (найвища вихідна вага):");
            var topGodNodes = outDegree.OrderByDescending(kvp => kvp.Value).Take(5).ToList();
            foreach (var god in topGodNodes)
            {
                sb.AppendLine($"  - [{god.Key}]: посилається на {god.Value} інших класів");
            }

            sb.AppendLine();

            // 3. Пошук ізольованих вузлів (Орфанів)
            var isolated = _graph.nodes
                .Where(n => inDegree[n.id] == 0 && outDegree[n.id] == 0)
                .ToList();

            sb.AppendLine($"Ізольовані класи (потенційно мертвий код: {isolated.Count}):");
            if (isolated.Count > 0)
            {
                foreach (var node in isolated.Take(15))
                {
                    sb.AppendLine($"  - {node.id} ({node.file})");
                }
                if (isolated.Count > 15)
                {
                    sb.AppendLine($"  ... та ще {isolated.Count - 15} класів.");
                }
            }
            else
            {
                sb.AppendLine("  (Ізольованих класів не знайдено)");
            }

            return sb.ToString();
        }

        public string FindCycles()
        {
            var states = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var pathStack = new List<string>();
            var cyclesList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in _graph.nodes)
            {
                states[node.id] = 0;
            }

            foreach (var node in _graph.nodes)
            {
                if (states[node.id] == 0)
                {
                    DfsCycleSearch(node.id, states, pathStack, cyclesList);
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== АНАЛІЗ ЦИКЛІЧНИХ ЗАЛЕЖНОСТЕЙ ===");
            if (cyclesList.Count == 0)
            {
                sb.AppendLine("[ОК] Архітектурних петель не виявлено! Код має чисту спрямовану структуру.");
            }
            else
            {
                sb.AppendLine($"[Попередження] Виявлено {cyclesList.Count} унікальних циклічних петель:");
                int index = 1;
                foreach (var cycle in cyclesList.OrderBy(c => c))
                {
                    sb.AppendLine($"{index++}. {cycle}");
                }
            }

            return sb.ToString();
        }

        private void DfsCycleSearch(string node, Dictionary<string, int> states, List<string> pathStack, HashSet<string> cyclesList)
        {
            states[node] = 1;
            pathStack.Add(node);

            if (_adjacencyList.TryGetValue(node, out var edges))
            {
                foreach (var edge in edges)
                {
                    string target = edge.target;

                    if (!states.ContainsKey(target)) continue;

                    if (states[target] == 1)
                    {
                        int cycleStartIndex = pathStack.IndexOf(target);
                        if (cycleStartIndex != -1)
                        {
                            var cyclePath = pathStack.Skip(cycleStartIndex).ToList();
                            cyclePath.Add(target);

                            string normalizedCycle = NormalizeCycle(cyclePath);
                            cyclesList.Add(normalizedCycle);
                        }
                    }
                    else if (states[target] == 0)
                    {
                        DfsCycleSearch(target, states, pathStack, cyclesList);
                    }
                }
            }

            pathStack.RemoveAt(pathStack.Count - 1);
            states[node] = 2;
        }

        /// <summary>
        /// Приводить цикл до канонічного вигляду для дедублікації та додає деталі зв'язків
        /// </summary>
        private string NormalizeCycle(List<string> cycleNodes)
        {
            // Видаляємо останній замикаючий елемент для сортування
            var temp = cycleNodes.Take(cycleNodes.Count - 1).ToList();

            // Знаходимо лексикографічно найменший вузол
            string minNode = temp.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).First();
            int minIndex = temp.IndexOf(minNode);

            // Зсуваємо список так, щоб найменший вузол був першим
            var normalized = temp.Skip(minIndex).Concat(temp.Take(minIndex)).ToList();
            normalized.Add(minNode); // Додаємо замикання назад

            // Будуємо деталізований опис зв'язків циклу
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < normalized.Count - 1; i++)
            {
                string source = normalized[i];
                string target = normalized[i + 1];

                // Шукаємо зв'язок у кеші
                var edge = _graph.links.FirstOrDefault(l =>
                    string.Equals(l.source, source, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(l.target, target, StringComparison.OrdinalIgnoreCase));

                string edgeInfo = edge != null ? $"{edge.type}: {edge.details}" : "unknown connection";

                sb.Append($"[{source}] ──({edgeInfo})──> ");
            }
            sb.Append($"[{minNode}]");

            return sb.ToString();
        }
    }
}