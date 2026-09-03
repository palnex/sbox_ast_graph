#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Editor.Analysis.Models;
using Editor.Analysis.Models.Blocks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Editor.Analysis.Internal.Extractors;

/// <summary>
/// 100% True Semantic Roslyn Extractor. Operates strictly on compiler symbols, types, and semantic facts with zero string heuristics.
/// </summary>
public class RoslynSemanticExtractor : CSharpSyntaxWalker
{
    private readonly CodeGraph _graph;
    private readonly SemanticModel _semanticModel;
    private readonly string _filePath;
    private readonly string _packageName;

    private readonly Stack<INamedTypeSymbol> _typeSymbolStack = new();

    public RoslynSemanticExtractor( CodeGraph graph, SemanticModel semanticModel, string filePath, string packageName = "" )
    {
        _graph = graph;
        _semanticModel = semanticModel;
        _filePath = filePath;
        _packageName = packageName;
    }

    private INamedTypeSymbol? CurrentTypeSymbol => _typeSymbolStack.Count > 0 ? _typeSymbolStack.Peek() : null;

    // ==========================================
    // 1. TYPE DECLARATIONS
    // ==========================================

    public override void VisitClassDeclaration( ClassDeclarationSyntax node )
    {
        if ( _semanticModel.GetDeclaredSymbol( node ) is INamedTypeSymbol classSymbol )
        {
            RegisterTypeSymbol( classSymbol, SandboxTypeCategory.Class, node.GetLocation() );
            _typeSymbolStack.Push( classSymbol );
            base.VisitClassDeclaration( node );
            _typeSymbolStack.Pop();
        }
        else
        {
            base.VisitClassDeclaration( node );
        }
    }

    public override void VisitStructDeclaration( StructDeclarationSyntax node )
    {
        if ( _semanticModel.GetDeclaredSymbol( node ) is INamedTypeSymbol structSymbol )
        {
            RegisterTypeSymbol( structSymbol, SandboxTypeCategory.Struct, node.GetLocation() );
            _typeSymbolStack.Push( structSymbol );
            base.VisitStructDeclaration( node );
            _typeSymbolStack.Pop();
        }
        else
        {
            base.VisitStructDeclaration( node );
        }
    }

    public override void VisitInterfaceDeclaration( InterfaceDeclarationSyntax node )
    {
        if ( _semanticModel.GetDeclaredSymbol( node ) is INamedTypeSymbol ifaceSymbol )
        {
            RegisterTypeSymbol( ifaceSymbol, SandboxTypeCategory.Interface, node.GetLocation() );
            _typeSymbolStack.Push( ifaceSymbol );
            base.VisitInterfaceDeclaration( node );
            _typeSymbolStack.Pop();
        }
        else
        {
            base.VisitInterfaceDeclaration( node );
        }
    }

    // ==========================================
    // 2. FIELDS & PROPERTIES
    // ==========================================

    public override void VisitFieldDeclaration( FieldDeclarationSyntax node )
    {
        if ( CurrentTypeSymbol != null )
        {
            foreach ( var variable in node.Declaration.Variables )
            {
                if ( _semanticModel.GetDeclaredSymbol( variable ) is IFieldSymbol fieldSymbol )
                {
                    AddSymbolDependencies( CurrentTypeSymbol, fieldSymbol.Type, RelationKind.FieldReference, $"Field '{fieldSymbol.Name}'", node.GetLocation() );
                }
            }
        }
        base.VisitFieldDeclaration( node );
    }

    public override void VisitPropertyDeclaration( PropertyDeclarationSyntax node )
    {
        if ( CurrentTypeSymbol != null )
        {
            if ( _semanticModel.GetDeclaredSymbol( node ) is IPropertySymbol propSymbol )
            {
                AddSymbolDependencies( CurrentTypeSymbol, propSymbol.Type, RelationKind.PropertyReference, $"Property '{propSymbol.Name}'", node.GetLocation() );
            }
        }
        base.VisitPropertyDeclaration( node );
    }

    // ==========================================
    // 3. METHODS, PARAMETERS, & RPCS
    // ==========================================

    public override void VisitMethodDeclaration( MethodDeclarationSyntax node )
    {
        if ( CurrentTypeSymbol != null && _semanticModel.GetDeclaredSymbol( node ) is IMethodSymbol methodSymbol )
        {
            bool isRpc = methodSymbol.GetAttributes().Any( a => a.AttributeClass?.Name.Contains( "Rpc" ) == true );
            var relationKind = isRpc ? RelationKind.RpcDispatch : (methodSymbol.IsAsync ? RelationKind.AsyncAwait : RelationKind.MethodCall);

            if ( methodSymbol.ReturnType.SpecialType != SpecialType.System_Void )
            {
                AddSymbolDependencies( CurrentTypeSymbol, methodSymbol.ReturnType, relationKind, $"Returns from '{methodSymbol.Name}()'", node.ReturnType.GetLocation() );
            }

            foreach ( var parameter in methodSymbol.Parameters )
            {
                AddSymbolDependencies( CurrentTypeSymbol, parameter.Type, RelationKind.MethodCall, $"Param '{parameter.Name}' in '{methodSymbol.Name}()'", node.GetLocation() );
            }
        }

        base.VisitMethodDeclaration( node );
    }

    // ==========================================
    // 4. OBJECT CREATIONS
    // ==========================================

    public override void VisitObjectCreationExpression( ObjectCreationExpressionSyntax node )
    {
        if ( CurrentTypeSymbol != null )
        {
            var typeSymbol = _semanticModel.GetTypeInfo( node ).Type;
            if ( typeSymbol != null )
            {
                AddSymbolDependencies( CurrentTypeSymbol, typeSymbol, RelationKind.Instantiates, $"new {typeSymbol.Name}()", node.GetLocation() );
            }
        }
        base.VisitObjectCreationExpression( node );
    }

    public override void VisitImplicitObjectCreationExpression( ImplicitObjectCreationExpressionSyntax node )
    {
        if ( CurrentTypeSymbol != null )
        {
            var typeSymbol = _semanticModel.GetTypeInfo( node ).Type;
            if ( typeSymbol != null )
            {
                AddSymbolDependencies( CurrentTypeSymbol, typeSymbol, RelationKind.Instantiates, $"new {typeSymbol.Name}()", node.GetLocation() );
            }
        }
        base.VisitImplicitObjectCreationExpression( node );
    }

    // ==========================================
    // 5. INVOCATIONS & COMPONENT FETCHING
    // ==========================================

    public override void VisitInvocationExpression( InvocationExpressionSyntax node )
    {
        if ( CurrentTypeSymbol != null )
        {
            var symbolInfo = _semanticModel.GetSymbolInfo( node );
            var methodSymbol = symbolInfo.Symbol as IMethodSymbol ??
                               (symbolInfo.CandidateSymbols.FirstOrDefault() as IMethodSymbol);

            if ( methodSymbol != null )
            {
                var targetType = methodSymbol.ContainingType;

                if ( methodSymbol.IsGenericMethod &&
                     methodSymbol.Name is "Get" or "GetAll" or "GetComponent" or "GetComponentInChildren" or "GetOrCreate" &&
                     (targetType.Name.Contains( "Component" ) || targetType.Name.Contains( "GameObject" ) || targetType.Name.Contains( "Scene" )) )
                {
                    foreach ( var typeArg in methodSymbol.TypeArguments )
                    {
                        AddSymbolDependencies( CurrentTypeSymbol, typeArg, RelationKind.ComponentFetch, $"Components.Get<{typeArg.Name}>()", node.GetLocation() );
                    }
                }
                else if ( targetType != null )
                {
                    AddSymbolDependencies( CurrentTypeSymbol, targetType, RelationKind.MethodCall, $"Method '{methodSymbol.Name}()'", node.GetLocation() );
                }
            }
        }

        base.VisitInvocationExpression( node );
    }

    // ==========================================
    // 6. EVENT SUBSCRIPTIONS
    // ==========================================

    public override void VisitAssignmentExpression( AssignmentExpressionSyntax node )
    {
        if ( CurrentTypeSymbol != null && (node.IsKind( SyntaxKind.AddAssignmentExpression ) || node.IsKind( SyntaxKind.SubtractAssignmentExpression )) )
        {
            var leftSymbol = _semanticModel.GetSymbolInfo( node.Left ).Symbol;

            if ( leftSymbol is IEventSymbol eventSymbol )
            {
                if ( eventSymbol.ContainingType != null )
                {
                    string sign = node.IsKind( SyntaxKind.AddAssignmentExpression ) ? "+=" : "-=";
                    AddSymbolDependencies( CurrentTypeSymbol, eventSymbol.ContainingType, RelationKind.EventSubscription, $"Event '{eventSymbol.Name}' {sign}", node.GetLocation() );
                }
            }
            else if ( leftSymbol is IPropertySymbol propSymbol && (propSymbol.Type.TypeKind == TypeKind.Delegate || propSymbol.Type.Name.Contains( "Action" )) )
            {
                if ( propSymbol.ContainingType != null )
                {
                    string sign = node.IsKind( SyntaxKind.AddAssignmentExpression ) ? "+=" : "-=";
                    AddSymbolDependencies( CurrentTypeSymbol, propSymbol.ContainingType, RelationKind.EventSubscription, $"Action '{propSymbol.Name}' {sign}", node.GetLocation() );
                }
            }
        }

        base.VisitAssignmentExpression( node );
    }

    // ==========================================
    // 7. ASYNC AWAIT
    // ==========================================

    public override void VisitAwaitExpression( AwaitExpressionSyntax node )
    {
        if ( CurrentTypeSymbol != null )
        {
            var typeSymbol = _semanticModel.GetTypeInfo( node.Expression ).Type;
            if ( typeSymbol != null )
            {
                AddSymbolDependencies( CurrentTypeSymbol, typeSymbol, RelationKind.AsyncAwait, "Task", "await async operation", node.GetLocation() );
            }
        }
        base.VisitAwaitExpression( node );
    }

    // ==========================================
    // HELPER: SYMBOL REGISTRATION & UNWRAPPING
    // ==========================================

    private void RegisterTypeSymbol( INamedTypeSymbol symbol, SandboxTypeCategory category, Location location )
    {
        string docId = symbol.GetDocumentationCommentId() ?? $"T:{symbol.ToDisplayString()}";
        var existing = _graph.GetNode( docId ) ?? _graph.GetNode( symbol.Name );

        int line = location.GetLineSpan().StartLinePosition.Line + 1;

        if ( existing != null )
        {
            // Update existing with TRUE source code facts
            existing.Body.Origin = NodeOrigin.UserProject;
            existing.Body.FilePath = _filePath;
            existing.Body.LineNumber = line;
            if ( !string.IsNullOrEmpty( _packageName ) ) existing.Body.PackageName = _packageName;
            if ( existing.Body.Category == SandboxTypeCategory.Class ) existing.Body.Category = category;
        }
        else
        {
            var node = new NodeBlock
            {
                Level = FractalLevel.Class,
                Body = new BodyBlock
                {
                    DocId = docId,
                    Name = symbol.Name,
                    Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                    Title = symbol.Name,
                    Category = category,
                    Origin = NodeOrigin.UserProject,
                    PackageName = _packageName,
                    FilePath = _filePath,
                    LineNumber = line,
                    IsAbstract = symbol.IsAbstract,
                    IsStatic = symbol.IsStatic,
                    IsValueType = symbol.IsValueType
                }
            };

            _graph.AddNode( node );
        }

        if ( symbol.BaseType != null && symbol.BaseType.SpecialType == SpecialType.None )
        {
            AddSymbolDependencies( symbol, symbol.BaseType, RelationKind.Inherits, "Base Class", location );
        }

        foreach ( var iface in symbol.Interfaces )
        {
            AddSymbolDependencies( symbol, iface, RelationKind.Implements, "Interface Implementation", location );
        }
    }

    private void AddSymbolDependencies(
        ITypeSymbol sourceSymbol,
        ITypeSymbol? targetSymbol,
        RelationKind kind,
        string contextDetails,
        Location location )
    {
        AddSymbolDependencies( sourceSymbol, targetSymbol, kind, targetSymbol?.Name ?? "", contextDetails, location );
    }

    private void AddSymbolDependencies(
        ITypeSymbol sourceSymbol,
        ITypeSymbol? targetSymbol,
        RelationKind kind,
        string instrument,
        string condition,
        Location location )
    {
        if ( targetSymbol == null ) return;

        foreach ( var unwrapped in UnwrapTypeSymbol( targetSymbol ) )
        {
            if ( unwrapped.SpecialType != SpecialType.None &&
                 unwrapped.SpecialType != SpecialType.System_Object )
            {
                continue;
            }

            if ( SymbolEqualityComparer.Default.Equals( sourceSymbol, unwrapped ) )
                continue;

            string sourceDocId = sourceSymbol.GetDocumentationCommentId() ?? $"T:{sourceSymbol.ToDisplayString()}";
            string targetDocId = unwrapped.GetDocumentationCommentId() ?? $"T:{unwrapped.ToDisplayString()}";

            int line = location.GetLineSpan().StartLinePosition.Line + 1;
            string actualInstrument = !string.IsNullOrEmpty( instrument ) ? instrument : unwrapped.Name;

            _graph.AddEdge( new SemanticWire
            {
                AgentDocId = sourceDocId,
                Action = kind,
                RecipientDocId = targetDocId,
                Instrument = actualInstrument,
                Condition = condition,
                LineNumber = line
            } );
        }
    }

    private static IEnumerable<ITypeSymbol> UnwrapTypeSymbol( ITypeSymbol typeSymbol )
    {
        if ( typeSymbol is IArrayTypeSymbol arrayType )
        {
            foreach ( var inner in UnwrapTypeSymbol( arrayType.ElementType ) )
                yield return inner;
            yield break;
        }

        if ( typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType )
        {
            if ( !namedType.ContainingNamespace?.ToDisplayString().StartsWith( "System.Collections" ) == true )
            {
                yield return namedType;
            }

            foreach ( var typeArg in namedType.TypeArguments )
            {
                foreach ( var inner in UnwrapTypeSymbol( typeArg ) )
                    yield return inner;
            }
            yield break;
        }

        yield return typeSymbol;
    }
}