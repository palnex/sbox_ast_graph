#nullable enable
using System;
using System.Collections.Generic;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Editor;
using Editor.Analysis;
using Editor.Analysis.Internal.Navigation;
using Editor.Analysis.Models;
using Editor.Analysis.Models.Blocks;
using Sandbox;

namespace ArchitectureVisualizer.UI.Floating;

/// <summary>
/// Comprehensive 4-Pillar Node Passport Inspector:
/// 1. Identity & Location (Body)
/// 2. Member Contracts & Anatomy (Members)
/// 3. Semantic Nervous Wires (Relations)
/// 4. Live Heat & Telemetry (Activity)
/// </summary>
public sealed class FloatingInspectorOverlay : Widget
{
    private readonly Label _titleLabel;
    private readonly Label _badgeLabel;
    private readonly Button _openIdeButton;
    private readonly Widget _contentContainer;

    private NodePayload? _currentPayload;
    public event Action<string>? OnNavigateRequested;

    public FloatingInspectorOverlay( Widget parent ) : base( parent )
    {
        FocusMode = FocusMode.Click;
        Cursor = CursorShape.Arrow;
        Size = new Vector2( 380, 480 );

        SetStyles( @"
            background-color: rgba( 15, 17, 23, 0.97 );
            border: 1px solid rgba( 255, 255, 255, 0.16 );
            border-radius: 8px;
            padding: 8px;
        " );

        Layout = Layout.Column();
        Layout.Margin = 6;
        Layout.Spacing = 4;

        // 1. Top Header Row
        var header = Layout.AddRow();
        _titleLabel = header.Add( new Label( "Node Identity", this ), 1 );
        _titleLabel.SetStyles( "font-weight: bold; font-size: 13px; color: #58a6ff;" );

        _badgeLabel = header.Add( new Label( "", this ) );
        _badgeLabel.SetStyles( "font-size: 9px; color: #8b949e; background: rgba(255,255,255,0.06); padding: 2px 6px; border-radius: 4px;" );

        var closeBtn = header.Add( new Button( "close", this ) );
        closeBtn.Clicked = () => Visible = false;
        closeBtn.FixedWidth = 20;
        closeBtn.FixedHeight = 20;

        // 2. Open in IDE Button
        _openIdeButton = Layout.Add( new Button( "Open in IDE", "code", this ) );
        _openIdeButton.Clicked = OnOpenInIdeClicked;
        _openIdeButton.FixedHeight = 24;

        // 3. Main Scrollable Container
        var scroll = Layout.Add( new ScrollArea( this ), 1 );
        scroll.Canvas = new Widget( scroll );
        scroll.Canvas.Layout = Layout.Column();
        scroll.Canvas.Layout.Spacing = 3;
        _contentContainer = scroll.Canvas;
    }

    public void Bind( NodePayload payload )
    {
        _currentPayload = payload;
        _titleLabel.Text = payload.Title;

        bool hasSource = !string.IsNullOrEmpty( payload.FilePath ) && CodeNavigator.ResolvePath( payload.FilePath ) != null;
        _openIdeButton.Enabled = hasSource;
        _openIdeButton.ToolTip = hasSource ? $"Open {payload.FilePath}:{payload.LineNumber}" : "Internal Engine Type (Source not on local disk)";

        _contentContainer.Layout.Clear( true );

        var node = payload.UserData as NodeBlock ?? CodeAnalysis.GetNode( payload.Id );
        if ( node == null ) return;

        var body = node.Body;
        _badgeLabel.Text = $"{body.Origin} | {body.Category}";

        // ==========================================
        // 1. 🏷️ IDENTITY & LOCATION (BODY)
        // ==========================================
        AddSectionHeader( "🏷️ IDENTITY & PASSPORT (Body)", "#58a6ff" );

        AddInfoRow( "DocId", body.DocId, "#a5d6ff" );
        if ( !string.IsNullOrEmpty( body.PackageName ) ) AddInfoRow( "Package", body.PackageName, "#d2a8ff" );
        if ( !string.IsNullOrEmpty( body.Namespace ) ) AddInfoRow( "Namespace", body.Namespace, "#8b949e" );
        if ( !string.IsNullOrEmpty( body.FilePath ) ) AddInfoRow( "Source", $"{body.FilePath}:{body.LineNumber}", "#7ee787" );

        string flags = "";
        if ( body.IsAbstract ) flags += "[abstract] ";
        if ( body.IsStatic ) flags += "[static] ";
        if ( body.IsValueType ) flags += "[struct] ";
        if ( !string.IsNullOrEmpty( flags ) ) AddInfoRow( "Flags", flags.Trim(), "#f1e05a" );

        if ( !string.IsNullOrWhiteSpace( body.Summary ) )
        {
            var summaryLbl = new Label( body.Summary.Trim(), _contentContainer );
            summaryLbl.SetStyles( "color: #c9d1d9; font-size: 10px; margin-top: 2px; margin-bottom: 4px;" );
            summaryLbl.WordWrap = true;
            _contentContainer.Layout.Add( summaryLbl );
        }

        // ==========================================
        // 2. ⚡ ANATOMY (MEMBERS)
        // ==========================================
        int totalMembers = node.Members.Methods.Count + node.Members.Properties.Count + node.Members.Fields.Count;
        AddSectionHeader( $"⚡ ANATOMY (Members: {totalMembers})", "#7ee787" );

        // Attributes
        if ( node.Attributes.Items.Count > 0 )
        {
            foreach ( var attr in node.Attributes.Items )
            {
                var attrLbl = new Label( $"  [{attr.Name}]", _contentContainer );
                attrLbl.SetStyles( "font-size: 9px; color: #f1e05a; font-family: Consolas, monospace;" );
                _contentContainer.Layout.Add( attrLbl );
            }
        }

        // Fields
        if ( node.Members.Fields.Count > 0 )
        {
            int shownFields = 0;
            foreach ( var field in node.Members.Fields )
            {
                if ( ++shownFields > 15 ) break;
                var fldLbl = new Label( $"  • {field.TypeName} {field.Name}", _contentContainer );
                fldLbl.SetStyles( "font-size: 10px; color: #8b949e; font-family: Consolas, monospace;" );
                _contentContainer.Layout.Add( fldLbl );
            }
        }

        // Properties
        if ( node.Members.Properties.Count > 0 )
        {
            int shownProps = 0;
            foreach ( var prop in node.Members.Properties )
            {
                if ( ++shownProps > 20 ) break;
                string propAttr = prop.HasPropertyAttribute ? "[Property] " : "";
                var propLbl = new Label( $"  {propAttr}{prop.TypeName} {prop.Name}", _contentContainer );
                propLbl.SetStyles( "font-size: 10px; color: #a5d6ff; font-family: Consolas, monospace;" );
                _contentContainer.Layout.Add( propLbl );
            }
        }

        // Methods
        if ( node.Members.Methods.Count > 0 )
        {
            int shownMethods = 0;
            foreach ( var method in node.Members.Methods )
            {
                if ( ++shownMethods > 25 ) break;
                var methLbl = new Label( $"  {method.FullSignature}", _contentContainer );
                methLbl.SetStyles( "font-size: 10px; color: #7ee787; font-family: Consolas, monospace;" );
                _contentContainer.Layout.Add( methLbl );
            }
        }

        // ==========================================
        // 3. 🔗 SEMANTIC WIRES (RELATIONS)
        // ==========================================
        AddSectionHeader( $"🔗 SEMANTIC WIRES (Out: {node.Relations.OutgoingCount} | In: {node.Relations.IncomingCount})", "#d2a8ff" );

        // Outgoing Wires (Who it calls/uses)
        if ( node.Relations.OutgoingCount > 0 )
        {
            int count = 0;
            foreach ( var edge in node.Relations.Outgoing )
            {
                if ( ++count > 25 ) break;

                string targetTitle = GetShortName( edge.RecipientDocId );
                string poly = edge.IsPolymorphicFanout ? " [poly]" : "";
                string label = $"─[{edge.Action}]─► {targetTitle}{poly}";
                if ( !string.IsNullOrEmpty( edge.Instrument ) ) label += $" ({edge.Instrument})";

                var btn = new Button( label, _contentContainer );
                btn.SetStyles( "font-size: 10px; padding: 2px 4px; text-align: left; color: #e6edf3; background: rgba(255,255,255,0.03); border-radius: 4px;" );
                string targetId = edge.RecipientDocId;
                btn.Clicked = () => OnNavigateRequested?.Invoke( targetId );
                _contentContainer.Layout.Add( btn );
            }

            if ( node.Relations.OutgoingCount > 25 )
            {
                var moreLbl = new Label( $"  ... and {node.Relations.OutgoingCount - 25} more outgoing wires", _contentContainer );
                moreLbl.SetStyles( "color: #8b949e; font-style: italic; font-size: 9px;" );
                _contentContainer.Layout.Add( moreLbl );
            }
        }

        // Incoming Wires (Who calls/uses it)
        if ( node.Relations.IncomingCount > 0 )
        {
            int inCount = 0;
            foreach ( var edge in node.Relations.Incoming )
            {
                if ( ++inCount > 15 ) break;

                string agentTitle = GetShortName( edge.AgentDocId );
                string label = $"◄─[{edge.Action}]─ {agentTitle}";

                var inBtn = new Button( label, _contentContainer );
                inBtn.SetStyles( "font-size: 10px; padding: 2px 4px; text-align: left; color: #a5d6ff; background: rgba(255,255,255,0.02); border-radius: 4px;" );
                string targetId = edge.AgentDocId;
                inBtn.Clicked = () => OnNavigateRequested?.Invoke( targetId );
                _contentContainer.Layout.Add( inBtn );
            }
        }

        // ==========================================
        // 4. 🔥 ACTIVITY & TELEMETRY (Placeholder)
        // ==========================================
        if ( node.Activity != null && node.Activity.InvocationCount > 0 )
        {
            AddSectionHeader( "🔥 ACTIVITY & TELEMETRY", "#ff7675" );
            AddInfoRow( "Invocations", $"{node.Activity.InvocationCount:N0}", "#ffffff" );
        }
    }

    private void AddSectionHeader( string title, string hexColor )
    {
        var header = new Label( title, _contentContainer );
        header.SetStyles( $"font-size: 9px; font-weight: bold; color: {hexColor}; margin-top: 8px; margin-bottom: 2px; border-bottom: 1px solid rgba(255,255,255,0.08); padding-bottom: 2px;" );
        _contentContainer.Layout.Add( header );
    }

    private void AddInfoRow( string key, string value, string valueHexColor )
    {
        var row = new Label( $"  {key}: {value}", _contentContainer );
        row.SetStyles( $"font-size: 10px; color: {valueHexColor}; font-family: Consolas, monospace;" );
        _contentContainer.Layout.Add( row );
    }

    private void OnOpenInIdeClicked()
    {
        if ( _currentPayload?.UserData is NodeBlock nodeBlock && nodeBlock.OpenInEditor() )
            return;

        if ( _currentPayload != null && !string.IsNullOrEmpty( _currentPayload.FilePath ) )
        {
            CodeNavigator.OpenFile( _currentPayload.FilePath, _currentPayload.LineNumber );
        }
    }

    private static string GetShortName( string docId )
    {
        if ( string.IsNullOrEmpty( docId ) ) return string.Empty;
        string clean = docId.StartsWith( "T:" ) || docId.StartsWith( "M:" ) || docId.StartsWith( "P:" ) || docId.StartsWith( "F:" ) ? docId[2..] : docId;
        int lastDot = clean.LastIndexOf( '.' );
        return lastDot >= 0 ? clean[(lastDot + 1)..] : clean;
    }

    protected override void OnMousePress( MouseEvent e ) => e.Accepted = true;
    protected override void OnMouseMove( MouseEvent e ) => e.Accepted = true;
    protected override void OnMouseWheel( WheelEvent e ) => e.Accepted = true;
}