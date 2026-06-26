using System.Collections.Generic;

namespace SboxAstGraph.Model
{
    /// <summary>
    /// Представляє окремий кастомний клас (вершину графу).
    /// </summary>
    public class CodeNode
    {
        public string Id { get; set; } = string.Empty;       // Назва класу (наприклад, "PlayerController")
        public string FilePath { get; set; } = string.Empty; // Шлях до файлу, де він оголошений
        public string Namespace { get; set; } = string.Empty;// Простір імен класу
    }

    /// <summary>
    /// Представляє спрямований зв'язок між двома класами (ребро графу).
    /// </summary>
    public class CodeEdge
    {
        public string Source { get; set; } = string.Empty;   // Звідки йде зв'язок (клас А)
        public string Target { get; set; } = string.Empty;   // Куди йде зв'язок (клас Б)
        public string Type { get; set; } = string.Empty;     // Тип зв'язку: "References", "CallsSingleton", "Subscribes"
        public string Details { get; set; } = string.Empty;  // Додаткові деталі (наприклад, назва методу чи події)
    }

    /// <summary>
    /// Повний граф архітектури проєкту.
    /// </summary>
    public class CodeGraph
    {
        public Dictionary<string, CodeNode> Nodes { get; } = new();
        public List<CodeEdge> Edges { get; } = new();

        public void AddNode(string id, string filePath, string @namespace)
        {
            if (!Nodes.ContainsKey(id))
            {
                Nodes[id] = new CodeNode
                {
                    Id = id,
                    FilePath = filePath,
                    Namespace = @namespace
                };
            }
        }

        public void AddEdge(string source, string target, string type, string details = "")
        {
            // Уникаємо самолінкування (клас посилається сам на себе)
            if (source == target) return;

            // Перевіряємо, чи такий зв'язок уже існує, щоб не дублювати його
            bool exists = Edges.Exists(e =>
                e.Source == source &&
                e.Target == target &&
                e.Type == type &&
                e.Details == details);

            if (!exists)
            {
                Edges.Add(new CodeEdge
                {
                    Source = source,
                    Target = target,
                    Type = type,
                    Details = details
                });
            }
        }
    }
}