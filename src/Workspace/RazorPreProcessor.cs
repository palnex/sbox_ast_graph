using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SboxAstGraph.Workspace
{
    public static class RazorPreProcessor
    {
        private static readonly Regex UsingRegex = new(@"@using\s+([A-Za-z0-9._]+);?", RegexOptions.Compiled);
        private static readonly Regex InheritsRegex = new(@"@inherits\s+([A-Za-z0-9._]+)", RegexOptions.Compiled);

        /// <summary>
        /// Конвертує .razor файл у валідний файл C# для аналізу через Roslyn.
        /// </summary>
        public static string? PreProcess(string razorContent, string className)
        {
            // 1. Збираємо всі директиви @using та конвертуємо їх у звичайні using C#
            var usings = new StringBuilder();
            usings.AppendLine("using System;"); // дефолтні імпорти
            usings.AppendLine("using Sandbox;");
            usings.AppendLine("using Sandbox.UI;");

            foreach (Match match in UsingRegex.Matches(razorContent))
            {
                string ns = match.Groups[1].Value;
                usings.AppendLine($"using {ns};");
            }

            // 2. Визначаємо базовий клас через @inherits (за замовчуванням Panel, якщо не знайдено)
            string baseClass = "Panel";
            var inheritsMatch = InheritsRegex.Match(razorContent);
            if (inheritsMatch.Success)
            {
                baseClass = inheritsMatch.Groups[1].Value;
            }

            // 3. Знаходимо та вилучаємо блок @code { ... }
            string? innerCode = ExtractCodeBlock(razorContent);
            if (string.IsNullOrEmpty(innerCode))
            {
                return null; // Якщо немає C# логіки, ігноруємо цей файл
            }

            // 4. Формуємо підсумковий валідний C# клас
            var finalCode = new StringBuilder();
            finalCode.AppendLine(usings.ToString());
            finalCode.AppendLine();
            finalCode.AppendLine("namespace SboxGeneratedRazorSpace");
            finalCode.AppendLine("{");
            finalCode.AppendLine($"    public partial class {className} : {baseClass}");
            finalCode.AppendLine("    {");
            finalCode.AppendLine(innerCode);
            finalCode.AppendLine("    }");
            finalCode.AppendLine("}");

            return finalCode.ToString();
        }

        /// <summary>
        /// Надійно витягує вміст блоку @code, враховуючи вкладені дужки { } всередині C# коду.
        /// </summary>
        private static string? ExtractCodeBlock(string content)
        {
            int index = content.IndexOf("@code");
            if (index == -1) return null;

            int openBraceIndex = content.IndexOf('{', index);
            if (openBraceIndex == -1) return null;

            int braceCount = 1;
            int currentIndex = openBraceIndex + 1;

            while (braceCount > 0 && currentIndex < content.Length)
            {
                if (content[currentIndex] == '{') braceCount++;
                else if (content[currentIndex] == '}') braceCount--;
                currentIndex++;
            }

            if (braceCount == 0)
            {
                // Повертаємо чистий C# код без зовнішніх дужок @code { ... }
                return content.Substring(openBraceIndex + 1, currentIndex - openBraceIndex - 2);
            }

            return null;
        }
    }
}