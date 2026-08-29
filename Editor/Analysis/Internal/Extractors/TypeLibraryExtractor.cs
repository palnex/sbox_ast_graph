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
/// Extracts runtime & editor type definitions, DocIds, member contracts, network realms, and polymorphic interfaces.
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
            if ( targetType == null ) continue;

            string typeFqn = GetTypeFqn( targetType );
            string typeDocId = TypeResolver.MakeTypeDocId( typeFqn );
            var origin = DetermineOrigin( targetType.Assembly );
            var category = DetermineCategory( targetType );
            var display = DisplayInfo.ForType( targetType );

            var node = new NodeBlock
            {
                Level = FractalLevel.Class
            };

            // 1. Fill BODY
            string displayTitle = !string.IsNullOrWhiteSpace( typeDesc.Title ) ? typeDesc.Title : display.Name;
            if ( string.IsNullOrWhiteSpace( displayTitle ) ) displayTitle = typeDesc.Name;

            var networkRealm = DetermineNetworkRealm( targetType );

            node.Body = new BodyBlock
            {
                DocId = typeDocId,
                Name = typeDesc.Name,
                Namespace = targetType.Namespace ?? string.Empty,
                Origin = origin,
                Category = category,
                Realm = networkRealm,
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

            // 2. Attributes
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

            // 3. Properties + Semantic Wires
            foreach ( var prop in typeDesc.Properties )
            {
                bool hasPropertyAttr = prop.HasAttribute<PropertyAttribute>();
                string propDocId = TypeResolver.MakePropertyDocId( typeFqn, prop.Name );

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
                        string targetDocId = TypeResolver.MakeTypeDocId( GetTypeFqn( unwrapped ) );

                        if ( !string.Equals( typeDocId, targetDocId, StringComparison.OrdinalIgnoreCase ) )
                        {
                            graph.AddEdge( new SemanticWire
                            {
                                AgentDocId = typeDocId,
                                Action = RelationKind.PropertyReference,
                                RecipientDocId = targetDocId,
                                Instrument = prop.PropertyType.Name,
                                Condition = $"Property '{prop.Name}'",
                                LineNumber = prop.SourceLine
                            } );
                        }
                    }
                }
            }

            // 4. Methods + Semantic Wires
            foreach ( var method in typeDesc.Methods )
            {
                var methodParamTypes = method.Parameters?.Select( p => p.ParameterType != null ? p.ParameterType.Name : "object" ).ToList() ?? new List<string>();
                string methodDocId = TypeResolver.MakeMethodDocId( typeFqn, method.Name, methodParamTypes );

                var methodItem = new MethodItem
                {
                    Name = method.Name,
                    ReturnTypeName = method.ReturnType != null ? GetCleanTypeName( method.ReturnType ) : "void",
                    Summary = method.Description ?? string.Empty,
                    IsStatic = method.IsStatic,
                    IsPublic = method.IsPublic,
                    SourceLine = method.SourceLine
                };

                // Method Return Type Wire
                if ( method.ReturnType != null && method.ReturnType != typeof( void ) )
                {
                    foreach ( var unwrapped in UnwrapTypes( method.ReturnType ) )
                    {
                        if ( !IsPrimitive( unwrapped ) )
                        {
                            string targetDocId = TypeResolver.MakeTypeDocId( GetTypeFqn( unwrapped ) );
                            if ( !string.Equals( typeDocId, targetDocId, StringComparison.OrdinalIgnoreCase ) )
                            {
                                graph.AddEdge( new SemanticWire
                                {
                                    AgentDocId = typeDocId,
                                    Action = RelationKind.MethodCall,
                                    RecipientDocId = targetDocId,
                                    Instrument = $"Returns {method.ReturnType.Name}",
                                    Condition = $"Method '{method.Name}()'",
                                    LineNumber = method.SourceLine
                                } );
                            }
                        }
                    }
                }

                // Method Parameter Wires
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
                                    string targetDocId = TypeResolver.MakeTypeDocId( GetTypeFqn( unwrapped ) );
                                    if ( !string.Equals( typeDocId, targetDocId, StringComparison.OrdinalIgnoreCase ) )
                                    {
                                        graph.AddEdge( new SemanticWire
                                        {
                                            AgentDocId = typeDocId,
                                            Action = RelationKind.MethodCall,
                                            RecipientDocId = targetDocId,
                                            Instrument = $"Param '{paramName}' ({param.ParameterType.Name})",
                                            Condition = $"Method '{method.Name}'",
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

            // 5. Fields
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
                            string targetDocId = TypeResolver.MakeTypeDocId( GetTypeFqn( unwrapped ) );
                            if ( !string.Equals( typeDocId, targetDocId, StringComparison.OrdinalIgnoreCase ) )
                            {
                                graph.AddEdge( new SemanticWire
                                {
                                    AgentDocId = typeDocId,
                                    Action = RelationKind.FieldReference,
                                    RecipientDocId = targetDocId,
                                    Instrument = field.FieldType.Name,
                                    Condition = $"Field '{field.Name}'",
                                    LineNumber = field.SourceLine
                                } );
                            }
                        }
                    }
                }
            }

            graph.AddNode( node );

            // 6. Base Class Inheritance Wire
            if ( typeDesc.BaseType?.TargetType != null && typeDesc.BaseType.TargetType != typeof( object ) )
            {
                graph.AddEdge( new SemanticWire
                {
                    AgentDocId = typeDocId,
                    Action = RelationKind.Inherits,
                    RecipientDocId = TypeResolver.MakeTypeDocId( GetTypeFqn( typeDesc.BaseType.TargetType ) ),
                    Condition = "Base Class",
                    LineNumber = typeDesc.SourceLine
                } );
            }

            // 7. Interface Implementation Wires + Polymorphic Indexing
            foreach ( var ifaceType in targetType.GetInterfaces() )
            {
                graph.AddEdge( new SemanticWire
                {
                    AgentDocId = typeDocId,
                    Action = RelationKind.Implements,
                    RecipientDocId = TypeResolver.MakeTypeDocId( GetTypeFqn( ifaceType ) ),
                    Condition = "Interface Implementation",
                    LineNumber = typeDesc.SourceLine
                } );
            }
        }
    }

    private static NetworkRealm DetermineNetworkRealm( Type type )
    {
        string str = type.ToString();
        if ( type.GetCustomAttributes().Any( a => a.GetType().Name.Contains( "Authority" ) || a.GetType().Name.Contains( "Host" ) ) )
            return NetworkRealm.HostOnly;
        if ( type.GetCustomAttributes().Any( a => a.GetType().Name.Contains( "Client" ) ) )
            return NetworkRealm.ClientOnly;
        return NetworkRealm.Shared;
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