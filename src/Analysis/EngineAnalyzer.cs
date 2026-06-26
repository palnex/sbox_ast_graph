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
        public Dictionary<string, ApiTypeNode> Registry { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        public CodeGraph Analyze(Schema schema)
        {
            var graph = new CodeGraph();
            Console.WriteLine("Побудова графу залежностей API S&box (Система 2 - Фаза 3)...");

            // 1. Фаза 1: Отримуємо багату модель типів без дублікатів та втрати перевантажень
            var apiParser = new EngineApiParser();
            var apiRegistry = apiParser.Parse(schema);

            this.Registry = apiRegistry;

            // 2. Реєструємо всі типи як вершини (Nodes) в графі (щоб створити для кожного індивідуальну сторінку)
            foreach (var kvp in apiRegistry)
            {
                var typeNode = kvp.Value;
                string cleanName = StubGenerator.SanitizeName(typeNode.Name);
                graph.AddNode(cleanName, "SboxEngine", typeNode.Namespace);
            }

            // 3. Фаза 3: Обходимо всі типи та вибудовуємо надточні зв'язки (Edges)
            foreach (var kvp in apiRegistry)
            {
                var sourceType = kvp.Value;
                string sourceCleanName = StubGenerator.SanitizeName(sourceType.Name);

                // А. Наслідування базового класу
                if (!string.IsNullOrEmpty(sourceType.BaseType))
                {
                    var baseSignature = TypeResolver.Parse(sourceType.BaseType);
                    LinkSignature(graph, apiRegistry, sourceCleanName, baseSignature, "Inherits", "Base Class");
                }

                // Б. Зв'язки через властивості
                foreach (var prop in sourceType.Properties.Values)
                {
                    var propSignature = TypeResolver.Parse(prop.PropertyType);
                    LinkSignature(graph, apiRegistry, sourceCleanName, propSignature, "References", $"Property: {prop.Name}");
                }

                // В. Зв'язки через поля
                foreach (var field in sourceType.Fields.Values)
                {
                    var fieldSignature = TypeResolver.Parse(field.FieldType);
                    LinkSignature(graph, apiRegistry, sourceCleanName, fieldSignature, "References", $"Field: {field.Name}");
                }

                // Г. Зв'язки через методи (аналізуємо типи повернення та параметри)
                foreach (var method in sourceType.Methods.Values)
                {
                    // Аналіз типу повернення методу
                    var returnSignature = TypeResolver.Parse(method.ReturnType);
                    LinkSignature(graph, apiRegistry, sourceCleanName, returnSignature, "References", $"Method Return: {method.Name}()");

                    // Аналіз кожного параметра методу
                    foreach (var param in method.Parameters)
                    {
                        var paramSignature = TypeResolver.Parse(param.ParameterType);
                        LinkSignature(graph, apiRegistry, sourceCleanName, paramSignature, "References", $"Method Param: {method.Name}({param.Name})");
                    }
                }
            }

            Console.WriteLine($"[ОК] Граф API побудовано: Вершин = {graph.Nodes.Count}, Зв'язків = {graph.Edges.Count}");
            return graph;
        }

        /// <summary>
        /// Рекурсивно обходить сигнатуру типу та створює логічні зв'язки в графі.
        /// </summary>
        private void LinkSignature(
            CodeGraph graph,
            Dictionary<string, ApiTypeNode> apiRegistry,
            string sourceCleanName,
            TypeSignature signature,
            string edgeType,
            string details)
        {
            if (signature == null) return;

            // 1. Шукаємо тип у нашому реєстрі за його FullName або CleanName
            ApiTypeNode? targetType = null;

            if (apiRegistry.TryGetValue(signature.FullName, out var foundByFull))
            {
                targetType = foundByFull;
            }
            else
            {
                // Шукаємо за коротким ім'ям у нашому реєстрі
                targetType = apiRegistry.Values.FirstOrDefault(t =>
                    string.Equals(t.Name, signature.CleanName, StringComparison.OrdinalIgnoreCase));
            }

            if (targetType != null)
            {
                // Наша головна фіча фільтрації: створюємо ребра тільки до класів/інтерфейсів.
                // Будь-які структури (Vector3, Color) та енуми ігноруються для побудови стрілок,
                // захищаючи граф від перевантаження.
                if (!targetType.IsValueType)
                {
                    string targetCleanName = StubGenerator.SanitizeName(targetType.Name);
                    graph.AddEdge(sourceCleanName, targetCleanName, edgeType, details);
                }
            }

            // 2. Рекурсивний обхід: витягуємо зв'язки з аргументів дженериків
            // Наприклад, для Dictionary<string, Enemy> ми знайдемо 'Enemy' та побудуємо до нього зв'язок
            foreach (var genericArg in signature.GenericArguments)
            {
                LinkSignature(graph, apiRegistry, sourceCleanName, genericArg, edgeType, details);
            }
        }
    }
}