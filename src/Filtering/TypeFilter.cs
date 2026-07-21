using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Facepunch.AssemblySchema; // Офіційна бібліотека Facepunch

namespace SboxAstGraph.Filtering; // Сучасний file-scoped namespace (C# 10+)

public class TypeFilter
{
    public static bool IncludeEngineLinks { get; set; } = false;

    // Набір типів, які ми ігноруємо примусово (стандартні примітиви C#)
    private static readonly HashSet<string> DefaultBlacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "Void", "Int32", "Int64", "Single", "Double", "Boolean", "String", "Char", "Object", "Decimal",
        "Byte", "SByte", "Int16", "UInt16", "UInt32", "UInt64", "IntPtr", "UIntPtr",
        "List", "Dictionary", "HashSet", "Action", "Func", "Task", "Type", "Guid", "DateTime", "TimeSpan"
    };

    // Сюди підвантажуватимуться типи двигуна S&box з api.json
    private readonly HashSet<string> _engineTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Конструктор фільтра. Приймає вже розпарсений об'єкт Schema для побудови списку ігнорування.
    /// </summary>
    public TypeFilter(Schema? schema = null)
    {
        if (schema != null)
        {
            LoadApiSchema(schema);
        }
    }

    /// <summary>
    /// Перевіряє, чи є тип системним примітивом або частиною двигуна S&box.
    /// </summary>
    public bool IsBlacklisted(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null) return true;

        // 1. Динамічно відсікаємо будь-які типи-значення (Structs, Enums, Primitives)
        // Це миттєво прибирає зв'язки до Vector3, Color, Rotation, int, float тощо
        if (typeSymbol.IsValueType)
        {
            return true;
        }

        // 2. Отримуємо чисту назву типу
        string typeName = typeSymbol.Name;

        // 3. Якщо тип є масивом або дженериком, дістаємо його внутрішній тип
        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            typeName = arrayType.ElementType.Name;
        }

        // 4. Перевірка за вбудованим чорним списком примітивів C# (для додаткової безпеки)
        if (DefaultBlacklist.Contains(typeName))
        {
            return true;
        }

        // 5. Перевірка за простором імен (ігноруємо все з System та Microsoft)
        string ns = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "";
        if (ns.StartsWith("System") || ns.StartsWith("Microsoft"))
        {
            return true;
        }

        // 6. Перевірка за офіційною схемою S&box
        if (_engineTypes.Contains(typeName) || _engineTypes.Contains($"{ns}.{typeName}") || ns.StartsWith("Sandbox"))
        {
            return true;
        }

        return false;
    }


    /// <summary>
    /// Розумно вирішує унікальне ім'я типу в API двигуна з урахуванням наявності чи відсутності простору назв.
    /// </summary>
    public string GetEngineFqn(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null) return string.Empty;

        string fqn = typeSymbol.ToDisplayString().Split('<')[0];
        string shortName = typeSymbol.Name;

        // Якщо в базі апішки цей тип зареєстрований суто за коротким ім'ям (наприклад, Vector3 замість Sandbox.Vector3)
        if (_engineTypes.Contains(shortName) && !_engineTypes.Contains(fqn))
        {
            return shortName; // Повертаємо чистий "Vector3", "Rotation" тощо
        }

        return fqn;
    }

    /// <summary>
    /// Заповнює чорний список типів двигуна S&box з уже готового об'єкта схеми.
    /// </summary>
    private void LoadApiSchema(Schema schema)
    {
        try
        {
            foreach (var type in schema.Types)
            {
                if (type != null)
                {
                    // Зберігаємо коротку та повну назву типу для максимальної точності фільтрації
                    if (!string.IsNullOrEmpty(type.Name)) _engineTypes.Add(type.Name);
                    if (!string.IsNullOrEmpty(type.FullName)) _engineTypes.Add(type.FullName);
                }
            }

            Console.WriteLine($"[ОК] Схему інтегровано у фільтр типів. Ігнорується офіційних типів двигуна: {_engineTypes.Count}");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[Попередження] Не вдалося обробити типи схеми у фільтрі: {ex.Message}");
            Console.ResetColor();
        }
    }

}