using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Facepunch.AssemblySchema; // Офіційна бібліотека Facepunch

namespace SboxAstGraph.Filtering; // Сучасний file-scoped namespace (C# 10+)

public class TypeFilter
{
    public static bool IncludeEngineLinks { get; set; } = true;

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
    /// Динамічно перевіряє, чи є назва типу офіційним типом двигуна з api.json (БЕЗ ХАРДКОДУ)
    /// </summary>
    public bool IsEngineType(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return _engineTypes.Contains(name);
    }

    /// <summary>
    /// Перевіряє, чи є тип системним примітивом (ігноруємо C# примітиви, але залишаємо S&box/Custom структури).
    /// </summary>
    public bool IsBlacklisted(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null) return true;

        string typeName = typeSymbol.Name;

        // 1. Якщо це масив, беремо внутрішній елемент
        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return IsBlacklisted(arrayType.ElementType);
        }

        // 2. Ігноруємо базові системні примітиви C# (int, float, bool тощо)
        if (DefaultBlacklist.Contains(typeName))
        {
            return true;
        }

        // 3. Ігноруємо все з системних просторів назв Microsoft/System
        string ns = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "";
        if (ns.StartsWith("System") || ns.StartsWith("Microsoft"))
        {
            return true;
        }

        // 4. Якщо це структура (ValueType), перевіряємо, чи це не S&box або локальний тип
        if (typeSymbol.IsValueType)
        {
            // Якщо це тип двигуна S&box або наш кастомний простір назв - НЕ блокуємо!
            if (IncludeEngineLinks && (ns.StartsWith("Sandbox") || ns.StartsWith("Editor")))
            {
                return false;
            }
            // Якщо це наш власний клас/структура (не системний primitive) - НЕ блокуємо!
            if (string.IsNullOrEmpty(ns) || !ns.StartsWith("System"))
            {
                return false;
            }
            return true; // Всі інші системні структури блокуємо
        }

        // 5. Перевірка за офіційною схемою S&box
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
                    if (!string.IsNullOrEmpty(type.Name)) _engineTypes.Add(type.Name);
                    if (!string.IsNullOrEmpty(type.FullName)) _engineTypes.Add(type.FullName);

                    // ДИНАМІЧНО: Реєструємо глобальні властивості-хелпери (Log, Input тощо) з Global* просторів
                    if (type.Properties != null && (type.Name?.Contains("Global") == true || type.FullName?.Contains("Global") == true))
                    {
                        foreach (var prop in type.Properties)
                        {
                            if (prop != null && !string.IsNullOrEmpty(prop.Name))
                            {
                                _engineTypes.Add(prop.Name); // Додає "Log", "Input" тощо як відомі двигуну символи!
                            }
                        }
                    }
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