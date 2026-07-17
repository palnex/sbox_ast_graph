using System;
using System.Collections.Generic;
using System.Linq;
using Facepunch.AssemblySchema;
using SboxAstGraph.Model;
using SboxAstGraph.Workspace;

namespace SboxAstGraph.Analysis
{
    public class EngineAnalyzer
    {
        // Реєстр багатих метаданих типів двигуна
        public Dictionary<string, ApiTypeNode> Registry { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        // Автоматично знайдені великі сімейства класів (наприклад, Sandbox.Component)
        public HashSet<string> LargeFamilies { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        // Кількість нащадків для кожного класу двигуна
        public Dictionary<string, int> DescendantCounts { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Допоміжний метод для генерації унікального імені файлу/вузла без колізій.
        /// Безпечно замінює всі заборонені символи ОС (Windows/Linux/macOS).
        /// </summary>
        public static string GetUniqueId(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "void";
            return fullName
                .Replace("+", "_")
                .Replace("`1", "")
                .Replace("`2", "")
                .Replace("`3", "")
                .Replace("`4", "")
                .Replace("`5", "")
                // Очищення заборонених символів Windows для імен файлів
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace(",", "_")
                .Replace(":", "_")
                .Replace("|", "_")
                .Replace("*", "_")
                .Replace("?", "_")
                .Replace("\"", "_")
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace(" ", "");
        }

        public CodeGraph Analyze(Schema schema)
        {
            var graph = new CodeGraph();
            Console.WriteLine("Побудова графу залежностей API S&box (Система 2)...");

            // 1. Отримуємо багату модель типів без дублікатів
            var apiParser = new EngineApiParser();
            var apiRegistry = apiParser.Parse(schema);
            this.Registry = apiRegistry;

            // 2. ДИНАМІЧНИЙ АНАЛІЗ НАСЛІДУВАННЯ (Без хардкоду)
            CalculateDescendantCounts(apiRegistry);

            // 3. Реєструємо всі типи як унікальні вершини в графі
            foreach (var kvp in apiRegistry)
            {
                var typeNode = kvp.Value;
                string uniqueId = GetUniqueId(typeNode.FullName);

                // Вказуємо унікальний ID, щоб Obsidian Graph View будував 100% точні лінії
                graph.AddNode(uniqueId, "SboxEngine", typeNode.Namespace);
            }

            // 4. Будуємо зв'язки між унікальними типами
            foreach (var kvp in apiRegistry)
            {
                var sourceType = kvp.Value;
                string sourceUniqueId = GetUniqueId(sourceType.FullName);

                // А. Наслідування базового класу
                if (!string.IsNullOrEmpty(sourceType.BaseType))
                {
                    var baseSignature = TypeResolver.Parse(sourceType.BaseType);
                    LinkSignature(graph, apiRegistry, sourceUniqueId, baseSignature, "Inherits", "Base Class");
                }

                // Б. Зв'язки через властивості
                foreach (var prop in sourceType.Properties.Values)
                {
                    var propSignature = TypeResolver.Parse(prop.PropertyType);
                    LinkSignature(graph, apiRegistry, sourceUniqueId, propSignature, "References", $"Property: {prop.Name}");
                }

                // В. Зв'язки через поля
                foreach (var field in sourceType.Fields.Values)
                {
                    var fieldSignature = TypeResolver.Parse(field.FieldType);
                    LinkSignature(graph, apiRegistry, sourceUniqueId, fieldSignature, "References", $"Field: {field.Name}");
                }

                // Г. Зв'язки через методи (типи повернення та параметри)
                foreach (var method in sourceType.Methods.Values)
                {
                    var returnSignature = TypeResolver.Parse(method.ReturnType);
                    LinkSignature(graph, apiRegistry, sourceUniqueId, returnSignature, "References", $"Method Return: {method.Name}()");

                    foreach (var param in method.Parameters)
                    {
                        var paramSignature = TypeResolver.Parse(param.ParameterType);
                        LinkSignature(graph, apiRegistry, sourceUniqueId, paramSignature, "References", $"Method Param: {method.Name}({param.Name})");
                    }
                }
            }

            Console.WriteLine($"[ОК] Граф API побудовано: Вершин = {graph.Nodes.Count}, Зв'язків = {graph.Edges.Count}");
            return graph;
        }

        /// <summary>
        /// Рекурсивно обчислює кількість нащадків для кожного класу двигуна, знаходячи великі родини.
        /// </summary>
        private void CalculateDescendantCounts(Dictionary<string, ApiTypeNode> apiRegistry)
        {
            DescendantCounts.Clear();
            LargeFamilies.Clear();

            foreach (var type in apiRegistry.Values)
            {
                DescendantCounts[type.FullName] = 0;
            }

            // Йдемо вгору по дереву наслідування для кожного типу
            foreach (var type in apiRegistry.Values)
            {
                string? currentBase = type.BaseType;
                int depth = 0; // Захист від нескінченних циклів

                while (!string.IsNullOrEmpty(currentBase) && depth < 25)
                {
                    if (apiRegistry.TryGetValue(currentBase, out var baseNode))
                    {
                        DescendantCounts[currentBase]++;
                        currentBase = baseNode.BaseType;
                    }
                    else
                    {
                        // Спробуємо знайти за коротким ім'ям у реєстрі, якщо повне ім'я базового типу не збіглося
                        var baseByShort = apiRegistry.Values.FirstOrDefault(t =>
                            string.Equals(t.Name, currentBase, StringComparison.OrdinalIgnoreCase));

                        if (baseByShort != null)
                        {
                            DescendantCounts[baseByShort.FullName]++;
                            currentBase = baseByShort.BaseType;
                        }
                        else
                        {
                            break;
                        }
                    }
                    depth++;
                }
            }

            // Маркуємо класи як великі родини, якщо вони мають більше 10 нащадків
            foreach (var kvp in DescendantCounts)
            {
                if (kvp.Value > 10)
                {
                    LargeFamilies.Add(kvp.Key);
                }
            }

            Console.WriteLine($"[Аналіз] Автоматично виявлено великих родин класів: {LargeFamilies.Count}");
        }

        /// <summary>
        /// Створює логічний зв'язок в графі між унікальними ідентифікаторами.
        /// </summary>
        private void LinkSignature(
            CodeGraph graph,
            Dictionary<string, ApiTypeNode> apiRegistry,
            string sourceUniqueId,
            TypeSignature signature,
            string edgeType,
            string details)
        {
            if (signature == null) return;

            ApiTypeNode? targetType = null;

            if (apiRegistry.TryGetValue(signature.FullName, out var foundByFull))
            {
                targetType = foundByFull;
            }
            else
            {
                targetType = apiRegistry.Values.FirstOrDefault(t =>
                    string.Equals(t.Name, signature.CleanName, StringComparison.OrdinalIgnoreCase));
            }

            if (targetType != null)
            {
                // Ігноруємо структури та значення, щоб не захаращувати граф лініями до Vector3, Color тощо
                if (!targetType.IsValueType)
                {
                    string targetUniqueId = GetUniqueId(targetType.FullName);
                    graph.AddEdge(sourceUniqueId, targetUniqueId, edgeType, details);
                }
            }

            // Рекурсивний обхід дженериків (наприклад, List<Player> -> пов'язуємо з Player)
            foreach (var genericArg in signature.GenericArguments)
            {
                LinkSignature(graph, apiRegistry, sourceUniqueId, genericArg, edgeType, details);
            }
        }
    }
}