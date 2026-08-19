#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Editor.Core.Models;
using Sandbox;
using Sandbox.UI;

namespace Editor.Core.Extractors;

/// <summary>
/// Extracts type definitions, metadata, and inheritance relationships from s&box TypeLibrary.
/// </summary>
public static class EngineTypeExtractor
{
    /// <summary>
    /// Scans all loaded types in TypeLibrary and registers them as nodes and inheritance edges in the graph.
    /// </summary>
    public static void ExtractAllTypes( DependencyGraph graph )
    {
        var allTypes = TypeLibrary.GetTypes();

        foreach ( var typeDesc in allTypes )
        {
            var targetType = typeDesc.TargetType;
            if ( targetType == null )
                continue;

            string id = GetTypeFqn( targetType );
            var origin = DetermineOrigin( targetType.Assembly );
            var category = DetermineCategory( targetType );
            var display = DisplayInfo.ForType( targetType );

            var node = new GraphNode
            {
                Id = id,
                Name = typeDesc.Name,
                Namespace = targetType.Namespace ?? string.Empty,
                Origin = origin,
                Category = category,
                Title = string.IsNullOrWhiteSpace( typeDesc.Title ) ? typeDesc.Name : typeDesc.Title,
                Summary = display.Description ?? string.Empty,
                Icon = display.Icon ?? string.Empty,
                Group = display.Group ?? string.Empty,
                IsAbstract = targetType.IsAbstract && !targetType.IsSealed,
                IsStatic = targetType.IsAbstract && targetType.IsSealed
            };

            // 1. Extract Properties
            foreach ( var prop in typeDesc.Properties )
            {
                node.Properties.Add( new MemberMetadata
                {
                    Name = prop.Name,
                    TypeName = prop.PropertyType?.Name ?? "object",
                    Description = prop.Description ?? string.Empty,
                    IsStatic = prop.IsStatic
                } );

                // Register field/property type reference edges (unwrapping generics)
                if ( prop.PropertyType != null )
                {
                    foreach ( var unwrapped in UnwrapTypes( prop.PropertyType ) )
                    {
                        string targetId = GetTypeFqn( unwrapped );
                        if ( !string.Equals( id, targetId, StringComparison.OrdinalIgnoreCase ) )
                        {
                            graph.AddEdge( new GraphEdge
                            {
                                SourceId = id,
                                TargetId = targetId,
                                Kind = RelationKind.PropertyReference,
                                Details = $"Property '{prop.Name}'"
                            } );
                        }
                    }
                }
            }

            // 2. Extract Methods
            foreach ( var method in typeDesc.Methods )
            {
                node.Methods.Add( new MemberMetadata
                {
                    Name = method.Name,
                    TypeName = method.ReturnType?.Name ?? "void",
                    Description = method.Description ?? string.Empty,
                    IsStatic = method.IsStatic
                } );
            }

            graph.AddNode( node );

            // 3. Extract Base Class Inheritance
            if ( typeDesc.BaseType?.TargetType != null && typeDesc.BaseType.TargetType != typeof( object ) )
            {
                graph.AddEdge( new GraphEdge
                {
                    SourceId = id,
                    TargetId = GetTypeFqn( typeDesc.BaseType.TargetType ),
                    Kind = RelationKind.Inherits,
                    Details = "Base Class"
                } );
            }

            // 4. Extract Interface Implementations
            foreach ( var iface in targetType.GetInterfaces() )
            {
                graph.AddEdge( new GraphEdge
                {
                    SourceId = id,
                    TargetId = GetTypeFqn( iface ),
                    Kind = RelationKind.Implements,
                    Details = "Interface Implementation"
                } );
            }
        }
    }

    /// <summary>
    /// Identifies the origin category based on the declaring assembly.
    /// </summary>
    public static NodeOrigin DetermineOrigin( Assembly assembly )
    {
        string name = assembly.GetName().Name ?? string.Empty;

        if ( name.StartsWith( "Sandbox." ) || name.StartsWith( "Facepunch." ) )
            return NodeOrigin.EngineRuntime;

        if ( name.StartsWith( "Editor." ) )
            return NodeOrigin.EngineEditor;

        if ( name.StartsWith( "System." ) || name.StartsWith( "Microsoft." ) || name.Equals( "mscorlib", StringComparison.OrdinalIgnoreCase ) )
            return NodeOrigin.SystemPrimitive;

        return NodeOrigin.UserProject;
    }

    /// <summary>
    /// Identifies the functional role of the type in s&box architecture.
    /// </summary>
    public static SandboxTypeCategory DetermineCategory( Type type )
    {
        if ( type.IsInterface ) return SandboxTypeCategory.Interface;
        if ( type.IsEnum ) return SandboxTypeCategory.Enum;
        if ( type.IsValueType ) return SandboxTypeCategory.Struct;

        if ( type.IsAssignableTo( typeof( PanelComponent ) ) ) return SandboxTypeCategory.UiPanelComponent;
        if ( type.IsAssignableTo( typeof( Component ) ) ) return SandboxTypeCategory.SceneComponent;
        if ( type.IsAssignableTo( typeof( Panel ) ) ) return SandboxTypeCategory.UiPanel;
        if ( type.IsAssignableTo( typeof( GameResource ) ) ) return SandboxTypeCategory.GameResource;

        return SandboxTypeCategory.Class;
    }

    /// <summary>
    /// Returns the fully qualified name without generic backticks (e.g. List`1).
    /// </summary>
    public static string GetTypeFqn( Type type )
    {
        if ( type.IsGenericType )
        {
            string cleanName = type.Name.Split( '`' )[0];
            return $"{type.Namespace}.{cleanName}";
        }
        return type.FullName ?? type.Name;
    }

    /// <summary>
    /// Recursively unwraps generics (List&lt;T&gt;, Dictionary&lt;K,V&gt;) and arrays into individual component types.
    /// </summary>
    public static IEnumerable<Type> UnwrapTypes( Type type )
    {
        if ( type == null ) yield break;

        if ( type.IsArray )
        {
            var el = type.GetElementType();
            if ( el != null )
            {
                foreach ( var inner in UnwrapTypes( el ) )
                    yield return inner;
            }
            yield break;
        }

        if ( type.IsGenericType )
        {
            foreach ( var arg in type.GetGenericArguments() )
            {
                foreach ( var inner in UnwrapTypes( arg ) )
                    yield return inner;
            }
            yield break;
        }

        yield return type;
    }
}