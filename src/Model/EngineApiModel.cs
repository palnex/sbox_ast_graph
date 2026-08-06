using System;
using System.Collections.Generic;

namespace SboxAstGraph.Model
{
    public class ApiTypeNode
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string? BaseType { get; set; }
        public bool IsInterface { get; set; }
        public bool IsEnum { get; set; }
        public bool IsValueType { get; set; }
        public bool IsAttribute { get; set; }
        public bool IsNested { get; set; }          // <--- НОВЕ ПОЛЕ: Чи є тип вкладеним
        public string? ParentType { get; set; }     // <--- НОВЕ ПОЛЕ: Повне ім'я батьківського класу
        public string? Summary { get; set; }

        // Використовуємо DocId як ключ для 100% унікальності та запобігання затиранню
        public Dictionary<string, ApiFieldNode> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ApiPropertyNode> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ApiMethodNode> Methods { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class ApiFieldNode
    {
        public string DocId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FieldType { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public bool IsStatic { get; set; }
        public string? Summary { get; set; }
    }

    public class ApiPropertyNode
    {
        public string DocId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PropertyType { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public bool IsStatic { get; set; }
        public string? Summary { get; set; }
    }

    public class ApiMethodNode
    {
        public string DocId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ReturnType { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public bool IsStatic { get; set; }
        public bool IsExtension { get; set; }
        public string? Summary { get; set; }
        public List<ApiParameterNode> Parameters { get; } = new();
    }

    public class ApiParameterNode
    {
        public string Name { get; set; } = string.Empty;
        public string ParameterType { get; set; } = string.Empty;
        public string? DefaultValue { get; set; }
    }
}