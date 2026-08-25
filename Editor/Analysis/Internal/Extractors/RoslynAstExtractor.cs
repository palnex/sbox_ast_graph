#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Editor.Analysis.Models;
using Editor.Analysis.Models.Blocks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Editor.Analysis.Internal.Extractors;

/// <summary>
/// Deep Roslyn AST Walker analyzing fields, properties, method signatures, local variables, component fetches, and calls.
/// </summary>
public class RoslynAstExtractor : CSharpSyntaxWalker
{
    private readonly CodeGraph _graph;
    private readonly string _filePath;

    private readonly Stack<string> _typeStack = new();

    // Local scope variable tracking: variableName -> TypeName (e.g. "spawner" -> "EnemySpawner")
    private readonly Dictionary<string, string> _localVariableTypes = new( StringComparer.OrdinalIgnoreCase );

    public RoslynAstExtractor( CodeGraph graph, string filePath )
    {
        _graph = graph;
        _filePath = filePath;
    }

    private string? CurrentType => _typeStack.Count > 0 ? _typeStack.Peek() : null;

    // ==========================================
    // 1. TYPE DECLARATIONS & BASE TYPES
    // ==========================================

    public override void VisitClassDeclaration( ClassDeclarationSyntax node )
    {
        string className = node.Identifier.Text;
        EnsureUserNodeRegistered( className, SandboxTypeCategory.Class, node.GetLocation() );

        // Extract Base types / Interfaces from declaration (e.g. class Player : Component, IDamageable)
        if ( node.BaseList != null )
        {
            foreach ( var baseType in node.BaseList.Types )
            {
                foreach ( var target in TypeResolver.ExtractTypes( baseType.Type ) )
                {
                    AddResolvedEdge( className, target, RelationKind.Inherits, "Base / Interface", baseType.GetLocation() );
                }
            }
        }

        _typeStack.Push( className );
        base.VisitClassDeclaration( node );
        _typeStack.Pop();
    }

    public override void VisitStructDeclaration( StructDeclarationSyntax node )
    {
        string structName = node.Identifier.Text;
        EnsureUserNodeRegistered( structName, SandboxTypeCategory.Struct, node.GetLocation() );

        _typeStack.Push( structName );
        base.VisitStructDeclaration( node );
        _typeStack.Pop();
    }

    public override void VisitInterfaceDeclaration( InterfaceDeclarationSyntax node )
    {
        string ifaceName = node.Identifier.Text;
        EnsureUserNodeRegistered( ifaceName, SandboxTypeCategory.Interface, node.GetLocation() );

        _typeStack.Push( ifaceName );
        base.VisitInterfaceDeclaration( node );
        _typeStack.Pop();
    }

    // ==========================================
    // 2. FIELDS & PROPERTIES DECLARATIONS
    // ==========================================

    public override void VisitFieldDeclaration( FieldDeclarationSyntax node )
    {
        if ( CurrentType != null )
        {
            foreach ( var target in TypeResolver.ExtractTypes( node.Declaration.Type ) )
            {
                string varNames = string.Join( ", ", node.Declaration.Variables.Select( v => v.Identifier.Text ) );
                AddResolvedEdge( CurrentType, target, RelationKind.FieldReference, $"Field '{varNames}'", node.GetLocation() );

                // Store field name -> type for variable scope resolution
                foreach ( var v in node.Declaration.Variables )
                {
                    _localVariableTypes[v.Identifier.Text] = target;
                }
            }
        }
        base.VisitFieldDeclaration( node );
    }

    public override void VisitPropertyDeclaration( PropertyDeclarationSyntax node )
    {
        if ( CurrentType != null )
        {
            foreach ( var target in TypeResolver.ExtractTypes( node.Type ) )
            {
                AddResolvedEdge( CurrentType, target, RelationKind.PropertyReference, $"Property '{node.Identifier.Text}'", node.GetLocation() );
                _localVariableTypes[node.Identifier.Text] = target;
            }
        }
        base.VisitPropertyDeclaration( node );
    }

    // ==========================================
    // 3. METHODS & CONSTRUCTOR SIGNATURES
    // ==========================================

    public override void VisitMethodDeclaration( MethodDeclarationSyntax node )
    {
        if ( CurrentType != null )
        {
            // A. Method Return Type
            foreach ( var retType in TypeResolver.ExtractTypes( node.ReturnType ) )
            {
                AddResolvedEdge( CurrentType, retType, RelationKind.MethodCall, $"Return type of '{node.Identifier.Text}()'", node.ReturnType.GetLocation() );
            }

            // B. Method Parameters
            foreach ( var param in node.ParameterList.Parameters )
            {
                if ( param.Type != null )
                {
                    foreach ( var paramType in TypeResolver.ExtractTypes( param.Type ) )
                    {
                        AddResolvedEdge( CurrentType, paramType, RelationKind.MethodCall, $"Param '{param.Identifier.Text}' in '{node.Identifier.Text}()'", param.GetLocation() );
                        _localVariableTypes[param.Identifier.Text] = paramType;
                    }
                }
            }
        }

        base.VisitMethodDeclaration( node );
    }

    public override void VisitConstructorDeclaration( ConstructorDeclarationSyntax node )
    {
        if ( CurrentType != null )
        {
            foreach ( var param in node.ParameterList.Parameters )
            {
                if ( param.Type != null )
                {
                    foreach ( var paramType in TypeResolver.ExtractTypes( param.Type ) )
                    {
                        AddResolvedEdge( CurrentType, paramType, RelationKind.MethodCall, $"Constructor param '{param.Identifier.Text}'", param.GetLocation() );
                        _localVariableTypes[param.Identifier.Text] = paramType;
                    }
                }
            }
        }

        base.VisitConstructorDeclaration( node );
    }

    // ==========================================
    // 4. LOCAL VARIABLE DECLARATIONS (var x = new Enemy())
    // ==========================================

    public override void VisitLocalDeclarationStatement( LocalDeclarationStatementSyntax node )
    {
        if ( CurrentType != null )
        {
            // Explicit type: Enemy e = ...
            foreach ( var targetType in TypeResolver.ExtractTypes( node.Declaration.Type ) )
            {
                foreach ( var variable in node.Declaration.Variables )
                {
                    _localVariableTypes[variable.Identifier.Text] = targetType;
                    AddResolvedEdge( CurrentType, targetType, RelationKind.FieldReference, $"Local '{variable.Identifier.Text}'", variable.GetLocation() );
                }
            }

            // Inferred type: var e = new Enemy() or var c = Components.Get<Camera>()
            foreach ( var variable in node.Declaration.Variables )
            {
                if ( variable.Initializer?.Value is ObjectCreationExpressionSyntax objCreation )
                {
                    foreach ( var targetType in TypeResolver.ExtractTypes( objCreation.Type ) )
                    {
                        _localVariableTypes[variable.Identifier.Text] = targetType;
                    }
                }
                else if ( variable.Initializer?.Value is InvocationExpressionSyntax invoc )
                {
                    string? inferred = TryInferComponentOrFactoryType( invoc );
                    if ( inferred != null )
                    {
                        _localVariableTypes[variable.Identifier.Text] = inferred;
                    }
                }
            }
        }

        base.VisitLocalDeclarationStatement( node );
    }

    // ==========================================
    // 5. OBJECT CREATION (new Monster(), new())
    // ==========================================

    public override void VisitObjectCreationExpression( ObjectCreationExpressionSyntax node )
    {
        if ( CurrentType != null )
        {
            foreach ( var target in TypeResolver.ExtractTypes( node.Type ) )
            {
                AddResolvedEdge( CurrentType, target, RelationKind.Instantiates, $"new {target}()", node.GetLocation() );
            }
        }
        base.VisitObjectCreationExpression( node );
    }

    public override void VisitImplicitObjectCreationExpression( ImplicitObjectCreationExpressionSyntax node )
    {
        if ( CurrentType != null && node.Parent is EqualsValueClauseSyntax eq && eq.Parent is VariableDeclaratorSyntax vd )
        {
            if ( _localVariableTypes.TryGetValue( vd.Identifier.Text, out var target ) )
            {
                AddResolvedEdge( CurrentType, target, RelationKind.Instantiates, $"new {target}()", node.GetLocation() );
            }
        }
        base.VisitImplicitObjectCreationExpression( node );
    }

    // ==========================================
    // 6. INVOCATIONS, COMPONENT FETCH, & MEMBER ACCESS
    // ==========================================

    public override void VisitInvocationExpression( InvocationExpressionSyntax node )
    {
        if ( CurrentType != null )
        {
            // A. Check for Components.Get<T>() / GetComponent<T>()
            if ( node.Expression is MemberAccessExpressionSyntax memberAccess )
            {
                string methodName = memberAccess.Name.Identifier.Text;

                if ( memberAccess.Name is GenericNameSyntax genericMethod )
                {
                    if ( methodName is "Get" or "GetAll" or "GetComponent" or "GetComponentInChildren" or "GetComponentInParent" or "GetOrCreate" )
                    {
                        foreach ( var arg in genericMethod.TypeArgumentList.Arguments )
                        {
                            foreach ( var compTarget in TypeResolver.ExtractTypes( arg ) )
                            {
                                AddResolvedEdge( CurrentType, compTarget, RelationKind.ComponentFetch, $"Components.Get<{compTarget}>()", node.GetLocation() );
                            }
                        }
                    }
                }
                else
                {
                    // B. Standard Method Invocations (target.Method() or Class.Method())
                    string callerExpr = memberAccess.Expression.ToString();

                    // 1. Try resolve variable from local/field tracker
                    if ( _localVariableTypes.TryGetValue( callerExpr, out var resolvedClass ) )
                    {
                        AddResolvedEdge( CurrentType, resolvedClass, RelationKind.MethodCall, $"Method '{methodName}()'", node.GetLocation() );
                    }
                    // 2. Try direct static type call (Sound.Play, GameManager.Reset)
                    else if ( !TypeResolver.IsPrimitive( callerExpr ) )
                    {
                        string cleanCaller = TypeResolver.GetShortName( callerExpr );
                        AddResolvedEdge( CurrentType, cleanCaller, RelationKind.MethodCall, $"Method '{methodName}()'", node.GetLocation() );
                    }
                }
            }
        }

        base.VisitInvocationExpression( node );
    }

    public override void VisitMemberAccessExpression( MemberAccessExpressionSyntax node )
    {
        if ( CurrentType != null )
        {
            string memberName = node.Name.Identifier.Text;

            // Singleton & Static Accessors (.Instance, .Current)
            if ( memberName is "Instance" or "Current" )
            {
                string targetType = node.Expression.ToString();
                if ( !TypeResolver.IsPrimitive( targetType ) )
                {
                    AddResolvedEdge( CurrentType, TypeResolver.GetShortName( targetType ), RelationKind.SingletonAccess, $".{memberName}", node.GetLocation() );
                }
            }
        }

        base.VisitMemberAccessExpression( node );
    }

    // ==========================================
    // 7. EVENT SUBSCRIPTIONS (+= / -=)
    // ==========================================

    public override void VisitAssignmentExpression( AssignmentExpressionSyntax node )
    {
        if ( CurrentType != null && (node.IsKind( SyntaxKind.AddAssignmentExpression ) || node.IsKind( SyntaxKind.SubtractAssignmentExpression )) )
        {
            if ( node.Left is MemberAccessExpressionSyntax memberAccess )
            {
                string eventName = memberAccess.Name.Identifier.Text;
                string caller = memberAccess.Expression.ToString();

                if ( _localVariableTypes.TryGetValue( caller, out var resolvedClass ) )
                {
                    string sign = node.IsKind( SyntaxKind.AddAssignmentExpression ) ? "+=" : "-=";
                    AddResolvedEdge( CurrentType, resolvedClass, RelationKind.EventSubscription, $"Event '{eventName}' {sign}", node.GetLocation() );
                }
                else if ( !TypeResolver.IsPrimitive( caller ) )
                {
                    string sign = node.IsKind( SyntaxKind.AddAssignmentExpression ) ? "+=" : "-=";
                    AddResolvedEdge( CurrentType, TypeResolver.GetShortName( caller ), RelationKind.EventSubscription, $"Event '{eventName}' {sign}", node.GetLocation() );
                }
            }
        }

        base.VisitAssignmentExpression( node );
    }

    // ==========================================
    // 8. PATTERN MATCHING & CASTS (is Monster, as Weapon)
    // ==========================================

    public override void VisitIsPatternExpression( IsPatternExpressionSyntax node )
    {
        if ( CurrentType != null && node.Pattern is DeclarationPatternSyntax declPattern )
        {
            foreach ( var target in TypeResolver.ExtractTypes( declPattern.Type ) )
            {
                AddResolvedEdge( CurrentType, target, RelationKind.FieldReference, $"is {target}", node.GetLocation() );
            }
        }
        base.VisitIsPatternExpression( node );
    }

    public override void VisitBinaryExpression( BinaryExpressionSyntax node )
    {
        if ( CurrentType != null && node.IsKind( SyntaxKind.AsExpression ) && node.Right is TypeSyntax typeSyntax )
        {
            foreach ( var target in TypeResolver.ExtractTypes( typeSyntax ) )
            {
                AddResolvedEdge( CurrentType, target, RelationKind.FieldReference, $"as {target}", node.GetLocation() );
            }
        }
        base.VisitBinaryExpression( node );
    }

    // ==========================================
    // HELPERS
    // ==========================================

    private string? TryInferComponentOrFactoryType( InvocationExpressionSyntax invoc )
    {
        if ( invoc.Expression is MemberAccessExpressionSyntax ma && ma.Name is GenericNameSyntax gn )
        {
            var firstArg = gn.TypeArgumentList.Arguments.FirstOrDefault();
            if ( firstArg != null )
            {
                return TypeResolver.ExtractTypes( firstArg ).FirstOrDefault();
            }
        }
        return null;
    }

    private void EnsureUserNodeRegistered( string typeName, SandboxTypeCategory category, Location location )
    {
        var existing = _graph.GetNode( typeName );
        int line = location.GetLineSpan().StartLinePosition.Line + 1;

        if ( existing == null )
        {
            var node = new NodeBlock();
            node.Header = new HeaderBlock
            {
                Id = typeName,
                Name = typeName,
                Title = typeName,
                Category = category,
                Origin = NodeOrigin.UserProject,
                FilePath = _filePath,
                LineNumber = line
            };
            _graph.AddNode( node );
        }
        else
        {
            if ( string.IsNullOrEmpty( existing.Header.FilePath ) )
            {
                existing.Header.FilePath = _filePath;
                existing.Header.LineNumber = line;
            }
        }
    }

    private void AddResolvedEdge( string source, string target, RelationKind kind, string details, Location location )
    {
        if ( string.Equals( source, target, StringComparison.OrdinalIgnoreCase ) )
            return;

        if ( TypeResolver.IsPrimitive( target ) )
            return;

        int line = location.GetLineSpan().StartLinePosition.Line + 1;

        string resolvedTargetId = target;
        var matchedNode = _graph.GetNode( target );
        if ( matchedNode != null )
        {
            resolvedTargetId = matchedNode.Id;
        }

        _graph.AddEdge( new GraphEdge
        {
            SourceId = source,
            TargetId = resolvedTargetId,
            Kind = kind,
            Details = details,
            LineNumber = line
        } );
    }
}