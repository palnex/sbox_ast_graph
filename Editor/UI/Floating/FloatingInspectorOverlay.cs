#nullable enable
using System;
using System.IO;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Editor;
using Editor.Analysis;
using Editor.Analysis.Models;
using Editor.Analysis.Models.Blocks;
using Sandbox;

namespace ArchitectureVisualizer.UI.Floating;

/// <summary>
/// Floating inspection card anchored to the selected node in world space.
/// </summary>
public sealed class FloatingInspectorOverlay : Widget
{
    private readonly Label _titleLabel;
    private readonly Label _namespaceLabel;
    private readonly Label _summaryLabel;
    private readonly Button _openIdeButton;
    private readonly Widget _depsContainer;

    private NodePayload? _currentPayload;
    public event Action<string>? OnNavigateRequested;

    public FloatingInspectorOverlay( Widget parent ) : base( parent )
    {
        FocusMode = FocusMode.Click;
        Cursor = CursorShape.Arrow;
        Size = new Vector2( 280, 260 );

        SetStyles( @"
            background-color: rgba( 18, 20, 26, 0.96 );
            border: 1px solid rgba( 255, 255, 255, 0.14 );
            border-radius: 8px;
            padding: 8px;
        " );

        Layout = Layout.Column();
        Layout.Margin = 6;
        Layout.Spacing = 4;

        var header = Layout.AddRow();
        _titleLabel = header.Add( new Label( "Node Title", this ), 1 );
        _titleLabel.SetStyles( "font-weight: bold; font-size: 12px; color: #ffffff;" );

        var closeBtn = header.Add( new Button( "close", this ) );
        closeBtn.Clicked = () => Visible = false;
        closeBtn.FixedWidth = 20;
        closeBtn.FixedHeight = 20;

        _namespaceLabel = Layout.Add( new Label( "", this ) );
        _namespaceLabel.SetStyles( "color: #8b949e; font-size: 10px;" );

        _openIdeButton = Layout.Add( new Button( "Open in IDE", "code", this ) );
        _openIdeButton.Clicked = OnOpenInIdeClicked;
        _openIdeButton.FixedHeight = 24;

        _summaryLabel = Layout.Add( new Label( "", this ) );
        _summaryLabel.SetStyles( "color: #c9d1d9; font-size: 10px;" );
        _summaryLabel.WordWrap = true;

        var scroll = Layout.Add( new ScrollArea( this ), 1 );
        scroll.Canvas = new Widget( scroll );
        scroll.Canvas.Layout = Layout.Column();
        scroll.Canvas.Layout.Spacing = 2;
        _depsContainer = scroll.Canvas;
    }

    public void Bind( NodePayload payload )
    {
        _currentPayload = payload;
        _titleLabel.Text = payload.Title;
        _namespaceLabel.Text = payload.Subtitle;
        _summaryLabel.Text = string.IsNullOrWhiteSpace( payload.Summary ) ? "No description." : payload.Summary.Trim();
        _openIdeButton.Enabled = !string.IsNullOrEmpty( payload.FilePath );

        _depsContainer.Layout.Clear( true );

        var node = payload.UserData as NodeBlock ?? CodeAnalysis.GetNode( payload.Id );
        if ( node != null )
        {
            foreach ( var edge in node.Relations.Outgoing )
            {
                var targetNode = CodeAnalysis.GetNode( edge.TargetId );
                string targetName = targetNode?.Header.Title ?? edge.TargetId;

                var btn = new Button( $"→ {targetName} ({edge.Kind})", _depsContainer );
                btn.SetStyles( "text-align: left; font-size: 10px; padding: 2px;" );
                string targetId = edge.TargetId;
                btn.Clicked = () => OnNavigateRequested?.Invoke( targetId );
                _depsContainer.Layout.Add( btn );
            }
        }

        AdjustSize();
    }

    private void OnOpenInIdeClicked()
    {
        if ( _currentPayload?.UserData is NodeBlock nodeBlock && nodeBlock.OpenInEditor() )
            return;

        if ( _currentPayload == null || string.IsNullOrEmpty( _currentPayload.FilePath ) ) return;
        string path = _currentPayload.FilePath;
        if ( !Path.IsPathRooted( path ) && Project.Current != null )
            path = Path.GetFullPath( Path.Combine( Project.Current.RootDirectory.FullName, path ) );

        if ( File.Exists( path ) ) CodeEditor.OpenFile( path, _currentPayload.LineNumber );
    }

    protected override void OnMousePress( MouseEvent e ) => e.Accepted = true;
    protected override void OnMouseMove( MouseEvent e ) => e.Accepted = true;
    protected override void OnMouseWheel( WheelEvent e ) => e.Accepted = true;
}