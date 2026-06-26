using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SboxAstGraph.Filtering;
using SboxAstGraph.Model;

namespace SboxAstGraph.Analysis
{
    public class CodeAnalyzer
    {
        private readonly TypeFilter _filter;

        public CodeAnalyzer(TypeFilter filter)
        {
            _filter = filter;
        }

        public CodeGraph Analyze(CSharpCompilation compilation)
        {
            var graph = new CodeGraph();
            var stopwatch = Stopwatch.StartNew();

            Console.WriteLine("Запуск двопрохідного аналізу файлів...");

            // --- ПРОХІД 1: Сканування та реєстрація всіх локальних класів ---
            var knownClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                if (semanticModel == null) continue;

                var root = syntaxTree.GetRoot();
                // Шукаємо оголошення класів та записуємо їх назви
                var classDeclarations = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>();
                foreach (var classDecl in classDeclarations)
                {
                    string className = classDecl.Identifier.Text;
                    knownClasses.Add(className);

                    // Відразу реєструємо вершину в графі
                    var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);
                    string ns = classSymbol?.ContainingNamespace?.ToDisplayString() ?? "SboxGeneratedRazorSpace";
                    graph.AddNode(className, syntaxTree.FilePath, ns);
                }
            }

            Console.WriteLine($"[Прохід 1] Зареєстровано локальних класів: {knownClasses.Count}");

            // --- ПРОХІД 2: Глибокий аналіз зв'язків з фолбеком ---
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                if (semanticModel == null) continue;

                // 1. Стандартний аналіз C# коду через Walker
                var walker = new SemanticWalker(semanticModel, _filter, graph, syntaxTree.FilePath, knownClasses);
                var root = syntaxTree.GetRoot();
                walker.Visit(root);

                // 2. ДОДАТКОВО: Якщо це Razor-файл, аналізуємо його HTML-розмітку на наявність вкладених компонентів та викликів логіки
                if (syntaxTree.FilePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string rawText = File.ReadAllText(syntaxTree.FilePath);
                        string currentComponent = Path.GetFileNameWithoutExtension(syntaxTree.FilePath);

                        // Отримуємо суто HTML-частину верстки (все, що лежить ДО блоку @code)
                        int codeIndex = rawText.IndexOf("@code");
                        string htmlMarkup = codeIndex != -1 ? rawText.Substring(0, codeIndex) : rawText;

                        foreach (var knownClass in knownClasses)
                        {
                            if (knownClass == currentComponent) continue;

                            // А. Шукаємо теги вкладених UI-компонентів: <UpgradeNode
                            string tagPattern = $@"<{knownClass}(\s|>|/)";
                            if (Regex.IsMatch(htmlMarkup, tagPattern, RegexOptions.IgnoreCase))
                            {
                                graph.AddEdge(currentComponent, knownClass, "UI_NestedComponent", "HTML Tag");
                            }

                            // Б. Шукаємо C# виклики логіки у верстці: @Formulas. чи @GameMetadata.
                            string expressionPattern = $@"@{knownClass}\.";
                            if (Regex.IsMatch(htmlMarkup, expressionPattern, RegexOptions.IgnoreCase))
                            {
                                graph.AddEdge(currentComponent, knownClass, "Calls", "HTML Expression");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Попередження] Не вдалося проаналізувати розмітку {syntaxTree.FilePath}: {ex.Message}");
                    }
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"[ОК] Аналіз завершено за {stopwatch.ElapsedMilliseconds} мс.");
            Console.WriteLine($"Побудовано граф: Вершин (Nodes) = {graph.Nodes.Count}, Зв'язків (Edges) = {graph.Edges.Count}");

            return graph;
        }
    }
}