#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Editor.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Editor.Core.Extractors;

/// <summary>
/// Roslyn AST Walker that inspects user C# syntax trees to discover call graphs, instantiations, event bindings, and singletons.
/// </summary>
public class RoslynAstWalker : CSharpSyntaxWalker
{
    private readonly DependencyGraph _graph;
    private readonly string _filePath;
    private readonly Stack<string> _typeContextStack = new();

    public RoslynAstWalker( DependencyGraph graph, string filePath )
    {
        _graph = graph;
        _filePath = filePath;
    }

    private string? CurrentType => _typeContextStack.Count > 0 ? _typeContextStack.Peek() : null;

    public override void VisitClassDeclaration( ClassDeclarationSyntax node )
    {
        string className = node.Identifier.Text;
        RegisterDeclaredType( className, SandboxTypeCategory.Class );

        _typeContextStack.Push( className );
        base.VisitClassDeclaration( node );
        _typeContextStack.Pop();
    }

    public override void VisitStructDeclaration( StructDeclarationSyntax node )
    {
        string structName = node.Identifier.Text;
        RegisterDeclaredType( structName, SandboxTypeCategory.Struct );

        _typeContextStack.Push( structName );
        base.VisitStructDeclaration( node );
        _typeContextStack.Pop();
    }

    public override void VisitInterfaceDeclaration( InterfaceDeclarationSyntax node )
    {
        string ifaceName = node.Identifier.Text;
        RegisterDeclaredType( ifaceName, SandboxTypeCategory.Interface );

        _typeContextStack.Push( ifaceName );
        base.VisitInterfaceDeclaration( node );
        _typeContextStack.Pop();
    }

    // 1. Instantiations: new Weapon(), new BBox()
    public override void VisitObjectCreationExpression( ObjectCreationExpressionSyntax node )
    {
        if ( CurrentType != null )
        {
            string targetName = ExtractTypeName( node.Type );
            if ( !string.IsNullOrWhiteSpace( targetName ) )
            {
                AddResolvedEdge( CurrentType, targetName, RelationKind.Instantiates, $"new {targetName}()", node.GetLocation() );
            }
        }
        base.VisitObjectCreationExpression( node );
    }

    // 2. Method Invocations: enemy.TakeDamage(), Sound.Play()
    public override void VisitInvocationExpression( InvocationExpressionSyntax node )
    {
        if ( CurrentType != null && node.Expression is MemberAccessExpressionSyntax memberAccess )
        {
            string methodName = memberAccess.Name.Identifier.Text;
            string callerTypeName = ExtractTypeName( memberAccess.Expression );

            if ( !string.IsNullOrWhiteSpace( callerTypeName ) )
            {
                AddResolvedEdge( CurrentType, callerTypeName, RelationKind.MethodCall, $"Method '{methodName}()'", node.GetLocation() );
            }
        }
        base.VisitInvocationExpression( node );
    }

    // 3. Static / Singleton Access: GameManager.Instance, HUD.Current
    public override void VisitMemberAccessExpression( MemberAccessExpressionSyntax node )
    {
        if ( CurrentType != null )
        {
            string memberName = node.Name.Identifier.Text;
            if ( memberName is "Instance" or "Current" )
            {
                string targetType = ExtractTypeName( node.Expression );
                if ( !string.IsNullOrWhiteSpace( targetType ) )
                {
                    AddResolvedEdge( CurrentType, targetType, RelationKind.SingletonAccess, $".{memberName}", node.GetLocation() );
                }
            }
        }
        base.VisitMemberAccessExpression( node );
    }

    // 4. Event & Action Subscriptions: Player.OnKilled += HandleDeath
    public override void VisitAssignmentExpression( AssignmentExpressionSyntax node )
    {
        if ( CurrentType != null && (node.IsKind( SyntaxKind.AddAssignmentExpression ) || node.IsKind( SyntaxKind.SubtractAssignmentExpression )) )
        {
            if ( node.Left is MemberAccessExpressionSyntax memberAccess )
            {
                string eventName = memberAccess.Name.Identifier.Text;
                string targetType = ExtractTypeName( memberAccess.Expression );

                if ( !string.IsNullOrWhiteSpace( targetType ) )
                {
                    string sign = node.IsKind( SyntaxKind.AddAssignmentExpression ) ? "+=" : "-=";
                    AddResolvedEdge( CurrentType, targetType, RelationKind.EventSubscription, $"Event '{eventName}' {sign}", node.GetLocation() );
                }
            }
        }
        base.VisitAssignmentExpression( node );
    }

    private void RegisterDeclaredType( string typeName, SandboxTypeCategory category )
    {
        var existing = _graph.GetNode( typeName );
        if ( existing == null )
        {
            _graph.AddNode( new GraphNode
            {
                Id = typeName,
                Name = typeName,
                Category = category,
                Origin = NodeOrigin.UserProject,
                FilePath = _filePath
            } );
        }
        else if ( string.IsNullOrEmpty( existing.FilePath ) )
        {
            existing.FilePath = _filePath;
        }
    }

    private void AddResolvedEdge( string source, string target, RelationKind kind, string details, Location location )
    {
        if ( string.Equals( source, target, StringComparison.OrdinalIgnoreCase ) )
            return;

        int line = location.GetLineSpan().StartLinePosition.Line + 1;

        // Try exact match or match by simple name in Graph
        string resolvedTargetId = target;
        var matchedNode = _graph.Nodes.Values.FirstOrDefault( n => string.Equals( n.Name, target, StringComparison.OrdinalIgnoreCase ) );
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

    private static string ExtractTypeName( ExpressionSyntax expr )
    {
        if ( expr is IdentifierNameSyntax id )
            return id.Identifier.Text;

        if ( expr is QualifiedNameSyntax qn )
            return qn.ToString();

        if ( expr is MemberAccessExpressionSyntax ma )
            return ma.ToString();

        return expr.ToString();
    }
}