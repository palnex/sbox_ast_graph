using System;
using System.Collections.Generic;
using System.Text.RegularExpressions; // Додано для регулярних виразів
using SboxAstGraph.Model;

namespace SboxAstGraph.Analysis
{
    public static class TypeResolver
    {
        // Регулярний вираз, який знаходить та видаляє будь-які метадані в дужках [InAttribute] або (InAttribute)
        // але не чіпає порожні квадратні дужки масивів []
        private static readonly Regex MetadataCleanupRegex = new(@"\[[^\]]+\]|\([^\)]+\)", RegexOptions.Compiled);

        /// <summary>
        /// Рекурсивно розбирає рядок типу на чисті складові метаданих C#.
        /// </summary>
        public static TypeSignature Parse(string typeStr)
        {
            if (string.IsNullOrWhiteSpace(typeStr))
            {
                return new TypeSignature { RawName = "void", FullName = "System.Void", CleanName = "void" };
            }

            typeStr = typeStr.Trim();

            // 1. Попередньо вирізаємо системні компіляторні тіки дженериків (наприклад, IEnumerable`1 -> IEnumerable)
            int tick = typeStr.IndexOf('`');
            if (tick != -1)
            {
                int end = tick + 1;
                while (end < typeStr.Length && char.IsDigit(typeStr[end]))
                {
                    end++;
                }
                typeStr = typeStr.Remove(tick, end - tick);
            }

            // 2. Попередньо очищаємо тип від маркерів маршалінгу типу [InAttribute] або (InAttribute)
            typeStr = MetadataCleanupRegex.Replace(typeStr, "").Trim();


            // 2. Очищаємо від поодиноких дужок, які могли випадково залишитися
            typeStr = typeStr.Replace("(", "").Replace(")", "").Replace("[", "").Replace("]", "").Trim();

            var signature = new TypeSignature { RawName = typeStr };

            // 3. Визначаємо ByRef посилання (@, & або низькорівневі modreq / modopt)
            if (typeStr.Contains("@") || typeStr.Contains("&") || typeStr.Contains("modreq") || typeStr.Contains("modopt"))
            {
                signature.IsByRef = true;

                // Вирізаємо низькорівневі системні суфікси .NET, лишаючи тільки чисте ім'я типу
                typeStr = typeStr
                    .Replace("@", "")
                    .Replace("modreq", "")
                    .Replace("modopt", "")
                    .Replace("&", "")
                    .Trim();
            }

            // 4. Визначаємо масиви ([])
            if (typeStr.EndsWith("[]") || signature.RawName.Contains("[]"))
            {
                signature.IsArray = true;
                typeStr = typeStr.Replace("[]", "").Trim();
            }

            // 5. Визначаємо покажчики (*)
            if (typeStr.EndsWith("*"))
            {
                signature.IsPointer = true;
                typeStr = typeStr.Substring(0, typeStr.Length - 1).Trim();
            }

            // 6. Парсимо дженерики (наприклад, List<Sandbox.Component>)
            int openBrace = typeStr.IndexOf('<');
            if (openBrace != -1 && typeStr.EndsWith(">"))
            {
                signature.FullName = typeStr.Substring(0, openBrace).Trim();
                string argsStr = typeStr.Substring(openBrace + 1, typeStr.Length - openBrace - 2).Trim();

                var args = SplitGenericArguments(argsStr);
                foreach (var arg in args)
                {
                    signature.GenericArguments.Add(Parse(arg));
                }
            }
            else
            {
                signature.FullName = typeStr;
            }

            // Очищуємо коротке ім'я
            signature.CleanName = GetShortName(signature.FullName);

            return signature;
        }

        private static List<string> SplitGenericArguments(string argsStr)
        {
            var result = new List<string>();
            int bracketCount = 0;
            int lastStart = 0;

            for (int i = 0; i < argsStr.Length; i++)
            {
                char c = argsStr[i];
                if (c == '<') bracketCount++;
                else if (c == '>') bracketCount--;
                else if (c == ',' && bracketCount == 0)
                {
                    result.Add(argsStr.Substring(lastStart, i - lastStart).Trim());
                    lastStart = i + 1;
                }
            }

            if (lastStart < argsStr.Length)
            {
                result.Add(argsStr.Substring(lastStart).Trim());
            }

            return result;
        }

        private static string GetShortName(string fullName)
        {
            int tickIndex = fullName.IndexOf('`');
            if (tickIndex != -1)
            {
                fullName = fullName.Substring(0, tickIndex);
            }

            int lastDot = fullName.LastIndexOf('.');
            return lastDot == -1 ? fullName : fullName.Substring(lastDot + 1);
        }
    }
}