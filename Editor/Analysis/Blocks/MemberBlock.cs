#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Editor.Analysis.Models.Blocks;

/// <summary>
/// Describes a parameter in a method signature.
/// </summary>
public class ParameterItem
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public bool HasDefaultValue { get; set; }
    public string? DefaultValue { get; set; }

    public override string ToString() => HasDefaultValue ? $"{TypeName} {Name} = {DefaultValue}" : $"{TypeName} {Name}";
}

/// <summary>
/// Describes a property member.
/// </summary>
public class PropertyItem
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsStatic { get; set; }
    public bool IsPublic { get; set; }
    public bool HasPropertyAttribute { get; set; }
    public int SourceLine { get; set; }
}

/// <summary>
/// Describes a method member with full parameter signatures.
/// </summary>
public class MethodItem
{
    public string Name { get; set; } = string.Empty;
    public string ReturnTypeName { get; set; } = "void";
    public string Summary { get; set; } = string.Empty;
    public bool IsStatic { get; set; }
    public bool IsPublic { get; set; }
    public bool IsAbstract { get; set; }
    public int SourceLine { get; set; }
    public List<ParameterItem> Parameters { get; set; } = new();

    /// <summary>
    /// Formatted parameter signature: "float amount, DamageInfo info".
    /// </summary>
    public string ParametersSummary => string.Join( ", ", Parameters.Select( p => p.ToString() ) );

    /// <summary>
    /// Full method signature: "void TakeDamage( float amount, DamageInfo info )".
    /// </summary>
    public string FullSignature => $"{(IsStatic ? "static " : "")}{ReturnTypeName} {Name}( {ParametersSummary} )";
}

/// <summary>
/// Describes a field member.
/// </summary>
public class FieldItem
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public bool IsStatic { get; set; }
    public bool IsPublic { get; set; }
    public int SourceLine { get; set; }
}

/// <summary>
/// Data block containing all declared members (properties, methods, fields).
/// </summary>
public class MemberBlock
{
    public List<PropertyItem> Properties { get; set; } = new();
    public List<MethodItem> Methods { get; set; } = new();
    public List<FieldItem> Fields { get; set; } = new();

    public int TotalMembersCount => Properties.Count + Methods.Count + Fields.Count;
}