#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Editor.Analysis.Internal.Extractors;

/// <summary>
/// High-performance type name resolver and generic argument unwrapper for AST nodes.
/// </summary>
public static class TypeResolver
{
    private static readonly Regex MetadataCleanupRegex = new( @"\[[^\]]+\]|\([^\)]+\)", RegexOptions.Compiled );

    private static readonly HashSet<string> SystemPrimitives = new( StringComparer.OrdinalIgnoreCase )
    {
        "void", "int", "string", "bool", "float", "double", "byte", "sbyte", "short", "ushort",
        "uint", "long", "ulong", "char", "decimal", "object", "dynamic", "var",
        "Int32", "Int64", "Single", "Double", "Boolean", "String", "Byte", "Char", "Object",
        "Action", "Func", "Task", "ValueTask", "Guid", "DateTime", "TimeSpan", "Type",
        "List", "Dictionary", "HashSet", "IEnumerable", "IReadOnlyList", "ICollection",
        "Array", "Span", "ReadOnlySpan", "Memory", "ReadOnlyMemory"
    };

    /// <summary>
    /// Checks if a type name is a basic .NET primitive or generic collection container.
    /// </summary>
    public static bool IsPrimitive( string? name )
    {
        if ( string.IsNullOrWhiteSpace( name ) ) return true;
        return SystemPrimitives.Contains( name );
    }

    /// <summary>
    /// Extracts all target type names from a TypeSyntax node, recursively unwrapping generics (List&lt;Enemy&gt; -&gt; Enemy).
    /// </summary>
    public static IEnumerable<string> ExtractTypes( TypeSyntax? typeSyntax )
    {
        if ( typeSyntax == null ) yield break;

        switch ( typeSyntax )
        {
            case ArrayTypeSyntax arrayType:
                foreach ( var inner in ExtractTypes( arrayType.ElementType ) )
                    yield return inner;
                break;

            case NullableTypeSyntax nullableType:
                foreach ( var inner in ExtractTypes( nullableType.ElementType ) )
                    yield return inner;
                break;

            case GenericNameSyntax genericName:
                // Return generic container name if non-primitive
                string genericClean = genericName.Identifier.Text;
                if ( !IsPrimitive( genericClean ) )
                    yield return genericClean;

                // Unwrap generic type arguments (e.g., T in List<T>)
                foreach ( var arg in genericName.TypeArgumentList.Arguments )
                {
                    foreach ( var inner in ExtractTypes( arg ) )
                        yield return inner;
                }
                break;

            case QualifiedNameSyntax qualifiedName:
                yield return qualifiedName.Right.Identifier.Text;
                break;

            case IdentifierNameSyntax identifierName:
                string text = identifierName.Identifier.Text;
                if ( !IsPrimitive( text ) )
                    yield return text;
                break;

            default:
                string raw = typeSyntax.ToString();
                foreach ( var t in ParseRawTypeString( raw ) )
                    yield return t;
                break;
        }
    }

    /// <summary>
    /// Fallback parser for raw string signatures.
    /// </summary>
    public static IEnumerable<string> ParseRawTypeString( string? raw )
    {
        if ( string.IsNullOrWhiteSpace( raw ) ) yield break;

        string clean = MetadataCleanupRegex.Replace( raw, "" )
            .Replace( "?", "" )
            .Replace( "[]", "" )
            .Replace( "&", "" )
            .Replace( "*", "" )
            .Trim();

        int tick = clean.IndexOf( '`' );
        if ( tick != -1 ) clean = clean.Substring( 0, tick );

        int open = clean.IndexOf( '<' );
        if ( open != -1 && clean.EndsWith( ">" ) )
        {
            string main = clean.Substring( 0, open ).Trim();
            if ( !IsPrimitive( main ) ) yield return GetShortName( main );

            string inner = clean.Substring( open + 1, clean.Length - open - 2 );
            foreach ( var part in inner.Split( ',' ) )
            {
                foreach ( var sub in ParseRawTypeString( part.Trim() ) )
                    yield return sub;
            }
        }
        else
        {
            string shortName = GetShortName( clean );
            if ( !IsPrimitive( shortName ) )
                yield return shortName;
        }
    }

    public static string GetShortName( string fullName )
    {
        int lastDot = fullName.LastIndexOf( '.' );
        return lastDot == -1 ? fullName : fullName.Substring( lastDot + 1 );
    }
}