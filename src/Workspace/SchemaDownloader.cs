using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Facepunch.AssemblySchema;

namespace SboxAstGraph.Workspace
{
    public static class SchemaDownloader
    {
        private const string DefaultFileName = "api.json";

        /// <summary>
        /// Перевіряє наявність локального файлу схеми API та повертає розпарсений об'єкт.
        /// </summary>
        public static async Task<Schema?> GetLatestSchemaAsync(string? explicitUserPath = null)
        {
            string? activePath = null;

            // 1. Якщо користувач передав шлях через аргументи --api
            if (!string.IsNullOrEmpty(explicitUserPath) && File.Exists(explicitUserPath))
            {
                activePath = explicitUserPath;
            }
            // 2. Інакше шукаємо дефолтний api.json у папці запуску
            else if (File.Exists(DefaultFileName))
            {
                activePath = DefaultFileName;
            }

            if (string.IsNullOrEmpty(activePath))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Увага] Файл схеми API '{DefaultFileName}' не знайдено.");
                Console.WriteLine("  -> Система 1 (аналіз коду) працюватиме у звичайному режимі без семантичної точності.");
                Console.WriteLine("  -> Система 2 (генерація документації API) буде пропущена.");
                Console.ResetColor();
                return null;
            }

            Console.WriteLine($"[ОК] Використовується локальна схема API: {Path.GetFullPath(activePath)}");

            try
            {
                string jsonContent = await File.ReadAllTextAsync(activePath);
                var schema = JsonSerializer.Deserialize<Schema>(jsonContent);
                if (schema != null)
                {
                    schema.Rebuild();
                    return schema;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Помилка] Не вдалося зчитати схему з {activePath}: {ex.Message}");
                Console.ResetColor();
            }

            return null;
        }
    }
}