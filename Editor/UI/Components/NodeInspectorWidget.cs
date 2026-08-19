#nullable enable
using System;
using System.IO;
using Editor.Core.Models;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.Components;

/// <summary>
/// Inspector sidebar displaying rich metadata, XML summaries, IDE jumping, and connection lists for a selected node.
/// </summary>
public sealed class NodeInspectorWidget : Widget
{
    private CanvasNode? _activeCanvasNode;
    private GraphNode? _activeGraphNode;

    private readonly Label _titleLabel;
    private readonly Label _namespaceLabel;
    private readonly Label _categoryBadge;
    private readonly Label _summaryLabel;
    private readonly Label _fileInfoLabel;
    private readonly Button _openIdeButton;

    private readonly Widget _outgoingContainer;
    private readonly Widget _incomingContainer;

    public event Action<string>? OnNavigateToNodeRequested;

    public NodeInspectorWidget( Widget parent ) : base( parent )
    {
        Layout = Layout.Column();
        Layout.Margin = 12;
        Layout.Spacing = 8;

        // --- Header Section ---
        var headerRow = Layout.AddRow();
        headerRow.Spacing = 6;

        _categoryBadge = new Label( "Class", this );
        _categoryBadge.FixedHeight = 20;
        headerRow.Add( _categoryBadge );

        _titleLabel = new Label( "Select a node", this );
        headerRow.Add( _titleLabel, 1 );

        _namespaceLabel = Layout.Add( new Label( "Namespace: -", this ) );
        _fileInfoLabel = Layout.Add( new Label( "File: -", this ) );

        // --- Action Buttons ---
        _openIdeButton = Layout.Add( new Button( "Open in Code Editor", "code", this ) );
        _openIdeButton.Clicked = OnOpenInIdeClicked;
        _openIdeButton.Enabled = false;

        Layout.AddSpacingCell( 6 );

        // --- XML Summary ---
        Layout.Add( new Label( "SUMMARY", this ) );

        _summaryLabel = Layout.Add( new Label( "No summary available.", this ) );
        _summaryLabel.WordWrap = true;

        Layout.AddSpacingCell( 8 );

        // --- Scrollable Dependency Lists ---
        var scrollArea = Layout.Add( new ScrollArea( this ), 1 );
        scrollArea.Canvas = new Widget( scrollArea );
        scrollArea.Canvas.Layout = Layout.Column();
        scrollArea.Canvas.Layout.Spacing = 10;
        scrollArea.Canvas.Layout.Margin = 4;

        // Outgoing ("Depends On")
        scrollArea.Canvas.Layout.Add( new Label( "DEPENDS ON (Outgoing)", scrollArea.Canvas ) );

        _outgoingContainer = scrollArea.Canvas.Layout.Add( new Widget( scrollArea.Canvas ) );
        _outgoingContainer.Layout = Layout.Column();
        _outgoingContainer.Layout.Spacing = 4;

        // Incoming ("Used By")
        scrollArea.Canvas.Layout.Add( new Label( "USED BY (Incoming)", scrollArea.Canvas ) );

        _incomingContainer = scrollArea.Canvas.Layout.Add( new Widget( scrollArea.Canvas ) );
        _incomingContainer.Layout = Layout.Column();
        _incomingContainer.Layout.Spacing = 4;
    }

    /// <summary>
    /// Binds a CanvasNode and its underlying GraphNode to the inspector.
    /// </summary>
    public void InspectNode( CanvasNode? canvasNode, DependencyGraph? graph )
    {
        _activeCanvasNode = canvasNode;
        _activeGraphNode = canvasNode?.UserData as GraphNode;

        // Clear previous connection lists
        _outgoingContainer.DestroyChildren();
        _incomingContainer.DestroyChildren();

        if ( _activeGraphNode == null || canvasNode == null )
        {
            _titleLabel.Text = "Select a node";
            _namespaceLabel.Text = "Namespace: -";
            _fileInfoLabel.Text = "File: -";
            _categoryBadge.Text = "None";
            _summaryLabel.Text = "Click any node on the canvas to inspect its architecture details.";
            _openIdeButton.Enabled = false;
            return;
        }

        _titleLabel.Text = _activeGraphNode.Name;
        _namespaceLabel.Text = $"Namespace: {_activeGraphNode.Namespace}";
        _fileInfoLabel.Text = string.IsNullOrEmpty( _activeGraphNode.FilePath )
            ? "Origin: Engine Assembly"
            : $"File: {Path.GetFileName( _activeGraphNode.FilePath )}";

        _categoryBadge.Text = _activeGraphNode.Category.ToString();
        _summaryLabel.Text = string.IsNullOrWhiteSpace( _activeGraphNode.Summary )
            ? "No XML documentation summary provided for this type."
            : _activeGraphNode.Summary.Trim();

        _openIdeButton.Enabled = !string.IsNullOrEmpty( _activeGraphNode.FilePath );

        // Populate Connections if Graph is present
        if ( graph != null )
        {
            // Outgoing Edges
            var outgoing = graph.GetOutgoingEdges( _activeGraphNode.Id );
            int outCount = 0;
            foreach ( var edge in outgoing )
            {
                string targetName = graph.Nodes.TryGetValue( edge.TargetId, out var targetNode ) ? targetNode.Name : edge.TargetId;
                var row = CreateConnectionRow( "arrow_forward", targetName, edge.Kind.ToString(), edge.TargetId );
                _outgoingContainer.Layout.Add( row );
                outCount++;
            }
            if ( outCount == 0 )
            {
                _outgoingContainer.Layout.Add( new Label( "None (Independent leaf)", _outgoingContainer ) );
            }

            // Incoming Edges
            var incoming = graph.GetIncomingEdges( _activeGraphNode.Id );
            int inCount = 0;
            foreach ( var edge in incoming )
            {
                string sourceName = graph.Nodes.TryGetValue( edge.SourceId, out var sourceNode ) ? sourceNode.Name : edge.SourceId;
                var row = CreateConnectionRow( "arrow_back", sourceName, edge.Kind.ToString(), edge.SourceId );
                _incomingContainer.Layout.Add( row );
                inCount++;
            }
            if ( inCount == 0 )
            {
                _incomingContainer.Layout.Add( new Label( "None (Root caller / unused)", _incomingContainer ) );
            }
        }
    }

    private Widget CreateConnectionRow( string icon, string targetNodeName, string relationKind, string targetId )
    {
        var row = new Widget( this );
        row.Layout = Layout.Row();
        row.Layout.Spacing = 6;

        var btn = row.Layout.Add( new Button( targetNodeName, icon, row ), 1 );
        btn.Clicked = () => OnNavigateToNodeRequested?.Invoke( targetId );

        var kindBadge = row.Layout.Add( new Label( $"[{relationKind}]", row ) );

        return row;
    }

    private void OnOpenInIdeClicked()
    {
        if ( _activeGraphNode == null || string.IsNullOrEmpty( _activeGraphNode.FilePath ) )
            return;

        string fullPath = _activeGraphNode.FilePath;
        if ( !Path.IsPathRooted( fullPath ) && Project.Current != null )
        {
            fullPath = Path.GetFullPath( Path.Combine( Project.Current.RootDirectory.FullName, fullPath ) );
        }

        if ( File.Exists( fullPath ) )
        {
            CodeEditor.OpenFile( fullPath, 1 );
        }
    }
}