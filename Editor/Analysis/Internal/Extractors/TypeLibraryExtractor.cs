#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Editor.Analysis.Models;
using Editor.Analysis.Models.Blocks;
using Sandbox;
using Sandbox.UI;

namespace Editor.Analysis.Internal.Extractors;

/// <summary>
/// Extracts all engine and user type definitions, full member signatures, attributes, and deep hierarchy/call edges.
/// </summary>
public static class TypeLibraryExtractor
{
    public static void Extract( CodeGraph graph )
    {
        var allTypes = TypeLibrary.GetTypes()
            .Concat( EditorTypeLibrary.GetTypes() )
            .DistinctBy( t => t.FullName );

        foreach ( var typeDesc in allTypes )
        {
            var targetType = typeDesc.TargetType;
            if ( targetType == null )
                continue;

            string id = GetTypeFqn( targetType );
            var origin = DetermineOrigin( targetType.Assembly );
            var category = DetermineCategory( targetType );
            var display = DisplayInfo.ForType( targetType );

            var node = new NodeBlock();

            // 1. Fill Header Block
            string displayTitle = !string.IsNullOrWhiteSpace( typeDesc.Title ) ? typeDesc.Title : display.Name;
            if ( string.IsNullOrWhiteSpace( displayTitle ) ) displayTitle = typeDesc.Name;

            node.Header = new HeaderBlock
            {
                Id = id,
                Name = typeDesc.Name,
                Namespace = targetType.Namespace ?? string.Empty,
                Origin = origin,
                Category = category,
                Title = displayTitle,
                Summary = !string.IsNullOrWhiteSpace( display.Description ) ? display.Description : (typeDesc.Description ?? string.Empty),
                Icon = !string.IsNullOrWhiteSpace( display.Icon ) ? display.Icon : (typeDesc.Icon ?? string.Empty),
                Group = !string.IsNullOrWhiteSpace( display.Group ) ? display.Group : (typeDesc.Group ?? string.Empty),
                FilePath = typeDesc.SourceFile,
                LineNumber = typeDesc.SourceLine,
                IsAbstract = typeDesc.IsAbstract,
                IsStatic = typeDesc.IsStatic,
                IsValueType = typeDesc.IsValueType
            };

            // 2. Fill Attribute Block
            var rawAttributes = targetType.GetCustomAttributes( inherit: false );
            foreach ( var rawAttr in rawAttributes )
            {
                var attrType = rawAttr.GetType();
                node.Attributes.Items.Add( new AttributeItem
                {
                    Name = attrType.Name.Replace( "Attribute", "" ),
                    FullTypeName = attrType.FullName ?? attrType.Name,
                    Summary = rawAttr.ToString() ?? string.Empty
                } );
            }

            // 3. Properties + Property Reference Edges
            foreach ( var prop in typeDesc.Properties )
            {
                bool hasPropertyAttr = prop.HasAttribute<PropertyAttribute>();

                node.Members.Properties.Add( new PropertyItem
                {
                    Name = prop.Name,
                    TypeName = prop.PropertyType != null ? GetCleanTypeName( prop.PropertyType ) : "object",
                    Summary = prop.Description ?? string.Empty,
                    Group = prop.Group ?? string.Empty,
                    Order = prop.Order,
                    IsStatic = prop.IsStatic,
                    IsPublic = prop.IsPublic,
                    HasPropertyAttribute = hasPropertyAttr,
                    SourceLine = prop.SourceLine
                } );

                if ( prop.PropertyType != null )
                {
                    foreach ( var unwrapped in UnwrapTypes( prop.PropertyType ) )
                    {
                        if ( IsPrimitive( unwrapped ) ) continue;
                        string targetId = GetTypeFqn( unwrapped );
                        if ( !string.Equals( id, targetId, StringComparison.OrdinalIgnoreCase ) )
                        {
                            graph.AddEdge( new GraphEdge
                            {
                                SourceId = id,
                                TargetId = targetId,
                                Kind = RelationKind.PropertyReference,
                                Details = $"Property '{prop.Name}'",
                                LineNumber = prop.SourceLine
                            } );
                        }
                    }
                }
            }

            // 4. Methods + Return Type & Parameter Edges
            foreach ( var method in typeDesc.Methods )
            {
                var methodItem = new MethodItem
                {
                    Name = method.Name,
                    ReturnTypeName = method.ReturnType != null ? GetCleanTypeName( method.ReturnType ) : "void",
                    Summary = method.Description ?? string.Empty,
                    IsStatic = method.IsStatic,
                    IsPublic = method.IsPublic,
                    SourceLine = method.SourceLine
                };

                // Link Method Return Type
                if ( method.ReturnType != null && method.ReturnType != typeof( void ) )
                {
                    foreach ( var unwrapped in UnwrapTypes( method.ReturnType ) )
                    {
                        if ( !IsPrimitive( unwrapped ) )
                        {
                            string targetId = GetTypeFqn( unwrapped );
                            if ( !string.Equals( id, targetId, StringComparison.OrdinalIgnoreCase ) )
                            {
                                graph.AddEdge( new GraphEdge
                                {
                                    SourceId = id,
                                    TargetId = targetId,
                                    Kind = RelationKind.MethodCall,
                                    Details = $"Returns from '{method.Name}()'",
                                    LineNumber = method.SourceLine
                                } );
                            }
                        }
                    }
                }

                // Link Method Parameters
                if ( method.Parameters != null )
                {
                    foreach ( var param in method.Parameters )
                    {
                        string paramName = param.Name ?? "param";
                        methodItem.Parameters.Add( new ParameterItem
                        {
                            Name = paramName,
                            TypeName = param.ParameterType != null ? GetCleanTypeName( param.ParameterType ) : "object",
                            HasDefaultValue = param.HasDefaultValue,
                            DefaultValue = param.DefaultValue?.ToString()
                        } );

                        if ( param.ParameterType != null )
                        {
                            foreach ( var unwrapped in UnwrapTypes( param.ParameterType ) )
                            {
                                if ( !IsPrimitive( unwrapped ) )
                                {
                                    string targetId = GetTypeFqn( unwrapped );
                                    if ( !string.Equals( id, targetId, StringComparison.OrdinalIgnoreCase ) )
                                    {
                                        graph.AddEdge( new GraphEdge
                                        {
                                            SourceId = id,
                                            TargetId = targetId,
                                            Kind = RelationKind.MethodCall,
                                            Details = $"Param in '{method.Name}({paramName})'",
                                            LineNumber = method.SourceLine
                                        } );
                                    }
                                }
                            }
                        }
                    }
                }

                node.Members.Methods.Add( methodItem );
            }

            // 5. Fields + Field Type Edges
            foreach ( var field in typeDesc.Fields )
            {
                node.Members.Fields.Add( new FieldItem
                {
                    Name = field.Name,
                    TypeName = field.FieldType != null ? GetCleanTypeName( field.FieldType ) : "object",
                    Summary = field.Description ?? string.Empty,
                    IsStatic = field.IsStatic,
                    IsPublic = field.IsPublic,
                    SourceLine = field.SourceLine
                } );

                if ( field.FieldType != null )
                {
                    foreach ( var unwrapped in UnwrapTypes( field.FieldType ) )
                    {
                        if ( !IsPrimitive( unwrapped ) )
                        {
                            string targetId = GetTypeFqn( unwrapped );
                            if ( !string.Equals( id, targetId, StringComparison.OrdinalIgnoreCase ) )
                            {
                                graph.AddEdge( new GraphEdge
                                {
                                    SourceId = id,
                                    TargetId = targetId,
                                    Kind = RelationKind.FieldReference,
                                    Details = $"Field '{field.Name}'",
                                    LineNumber = field.SourceLine
                                } );
                            }
                        }
                    }
                }
            }

            graph.AddNode( node );

            // 6. Base Class Inheritance Edge
            if ( typeDesc.BaseType?.TargetType != null && typeDesc.BaseType.TargetType != typeof( object ) )
            {
                graph.AddEdge( new GraphEdge
                {
                    SourceId = id,
                    TargetId = GetTypeFqn( typeDesc.BaseType.TargetType ),
                    Kind = RelationKind.Inherits,
                    Details = "Base Class",
                    LineNumber = typeDesc.SourceLine
                } );
            }

            // 7. Interface Implementation Edges
            foreach ( var ifaceType in targetType.GetInterfaces() )
            {
                graph.AddEdge( new GraphEdge
                {
                    SourceId = id,
                    TargetId = GetTypeFqn( ifaceType ),
                    Kind = RelationKind.Implements,
                    Details = "Interface Implementation",
                    LineNumber = typeDesc.SourceLine
                } );
            }
        }
    }

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

    public static string GetTypeFqn( Type type )
    {
        if ( type.IsGenericType )
        {
            string cleanName = type.Name.Split( '`' )[0];
            return $"{type.Namespace}.{cleanName}";
        }
        return type.FullName ?? type.Name;
    }

    public static string GetCleanTypeName( Type type )
    {
        if ( type.IsGenericType )
        {
            string clean = type.Name.Split( '`' )[0];
            var args = string.Join( ", ", type.GetGenericArguments().Select( GetCleanTypeName ) );
            return $"{clean}<{args}>";
        }
        return type.Name;
    }

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

    private static bool IsPrimitive( Type type )
    {
        return type.IsPrimitive ||
               type == typeof( string ) ||
               type == typeof( object ) ||
               type == typeof( decimal ) ||
               type == typeof( void );
    }
}