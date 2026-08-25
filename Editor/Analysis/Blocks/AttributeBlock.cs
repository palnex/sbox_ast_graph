#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor.Analysis.Models.Blocks;

/// <summary>
/// Describes a single attribute decorating a class or member.
/// </summary>
public class AttributeItem
{
    public string Name { get; set; } = string.Empty;
    public string FullTypeName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;

    public override string ToString() => $"[{Name}]";
}

/// <summary>
/// Data block containing all custom attributes decorating the type.
/// </summary>
public class AttributeBlock
{
    public List<AttributeItem> Items { get; set; } = new();

    /// <summary>
    /// Checks if an attribute with the specified short or full name exists.
    /// </summary>
    public bool Has( string attributeName )
    {
        string clean = attributeName.EndsWith( "Attribute" ) ? attributeName : attributeName + "Attribute";
        return Items.Any( a =>
            string.Equals( a.Name, attributeName, StringComparison.OrdinalIgnoreCase ) ||
            string.Equals( a.Name, clean, StringComparison.OrdinalIgnoreCase ) );
    }
}