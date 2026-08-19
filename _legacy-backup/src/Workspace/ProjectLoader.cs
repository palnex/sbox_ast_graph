using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions; // Потрібно для обробки масок .astignore
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Facepunch.AssemblySchema;

namespace SboxAstGraph.Workspace
{
    public class ProjectLoader
    {
        // Папки, які ми точно ігноруємо за замовчуванням
        private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", ".git", ".vs", ".sandbox", "Properties", "temp"
        };

        private List<string> _customIgnorePatterns = new();

        /// <summary>
        /// Рекурсивно шукає всі файли .cs та .razor, враховуючи правила за замовчуванням та локальний .astignore
        /// </summary>
        public List<string> FindSourceFiles(string rootPath)
        {
            LoadAstIgnoreRules(rootPath);

            var sourceFiles = new List<string>();
            ScanDirectory(rootPath, sourceFiles, rootPath);
            return sourceFiles;
        }

        private void LoadAstIgnoreRules(string rootPath)
        {
            _customIgnorePatterns.Clear();
            string ignorePath = Path.Combine(rootPath, ".astignore");

            if (File.Exists(ignorePath))
            {
                Console.WriteLine($"[ОК] Зчитано правила фільтрації з: {ignorePath}");
                var lines = File.ReadAllLines(ignorePath);
                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    // Пропускаємо порожні рядки та коментарі
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                        continue;

                    // Нормалізуємо роздільники шляхів під універсальний Unix-стиль
                    _customIgnorePatterns.Add(trimmed.Replace('\\', '/'));
                }
            }
        }

        private bool IsPathIgnored(string fullPath, string rootPath)
        {
            string name = Path.GetFileName(fullPath);

            // 1. Базова перевірка системних папок
            if (IgnoredDirectories.Contains(name))
                return true;

            // Вираховуємо відносний шлях для порівняння з правилами
            string relativePath = Path.GetRelativePath(rootPath, fullPath).Replace('\\', '/');

            // 2. Перевірка користувацьких правил з .astignore
            foreach (var pattern in _customIgnorePatterns)
            {
                // Якщо правило закінчується на '/' (це папка)
                if (pattern.EndsWith("/"))
                {
                    string dirPattern = pattern.TrimEnd('/');
                    if (relativePath.Split('/').Contains(dirPattern, StringComparer.OrdinalIgnoreCase))
                        return true;
                }
                // Якщо правило містить маску '*'
                else if (pattern.Contains("*"))
                {
                    string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                    if (Regex.IsMatch(name, regexPattern, RegexOptions.IgnoreCase) ||
                        Regex.IsMatch(relativePath, regexPattern, RegexOptions.IgnoreCase))
                    {
                        return true;
                    }
                }
                // Пряме порівняння імені або шляху
                else
                {
                    if (relativePath.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                        relativePath.Split('/').Contains(pattern, StringComparer.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void ScanDirectory(string currentPath, List<string> results, string rootPath)
        {
            if (IsPathIgnored(currentPath, rootPath))
                return;

            try
            {
                var csFiles = Directory.GetFiles(currentPath, "*.cs");
                var razorFiles = Directory.GetFiles(currentPath, "*.razor");

                foreach (var file in csFiles)
                {
                    if (!IsPathIgnored(file, rootPath))
                        results.Add(file);
                }

                foreach (var file in razorFiles)
                {
                    if (!IsPathIgnored(file, rootPath))
                        results.Add(file);
                }

                var subDirs = Directory.GetDirectories(currentPath);
                foreach (var subDir in subDirs)
                {
                    ScanDirectory(subDir, results, rootPath);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Пропускаємо папки без доступу
            }
        }

        /// <summary>
        /// Створює об'єкт CSharpCompilation, об'єднуючи всі знайдені файли в єдину модель з підтримкою віртуальних заглушок API.
        /// </summary>
        public CSharpCompilation CreateCompilation(List<string> filePaths, Schema? schema = null)
        {
            var syntaxTrees = new List<SyntaxTree>();

            foreach (var path in filePaths)
            {
                try
                {
                    string extension = Path.GetExtension(path).ToLower();
                    string code = "";

                    if (extension == ".razor")
                    {
                        string className = Path.GetFileNameWithoutExtension(path);
                        string rawContent = File.ReadAllText(path);

                        // Використовуємо препроцесор
                        string? preProcessedCode = RazorPreProcessor.PreProcess(rawContent, className);

                        if (string.IsNullOrEmpty(preProcessedCode))
                        {
                            continue; // Пропускаємо Razor-файл без C# коду
                        }

                        code = preProcessedCode;
                    }
                    else
                    {
                        code = File.ReadAllText(path);
                    }

                    var syntaxTree = CSharpSyntaxTree.ParseText(code, path: path);
                    syntaxTrees.Add(syntaxTree);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[Попередження] Не вдалося зчитати файл {path}: {ex.Message}");
                    Console.ResetColor();
                }
            }

            var systemFolder = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location), // Виправлено: додано typeof
                MetadataReference.CreateFromFile(Path.Combine(systemFolder, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(Path.Combine(systemFolder, "System.Collections.dll"))
            };

            // ЯКЩО СХЕМА ДОСТУПНА: Генеруємо та додаємо референс на заглушки API S&box
            if (schema != null)
            {
                var sboxApiRef = StubGenerator.GenerateMetadataReference(schema);
                if (sboxApiRef != null)
                {
                    references.Add(sboxApiRef);
                }
            }

            return CSharpCompilation.Create(
                "SboxGameAssembly",
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
        }
    }
}