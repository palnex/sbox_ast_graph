#nullable enable
using System;
using Editor;
using Sandbox;


namespace ArchitectureVisualizer.UI.Floating;

/// <summary>
/// Minimalistic floating top HUD with search, scope pills, and node counters.
/// </summary>
public sealed class CanvasTopHud : Widget
{
    private readonly LineEdit _searchBox;
    private readonly Button _btnUser;
    private readonly Button _btnSystem;
    private readonly Button _btnComponents;
    private readonly Button _btnRazor;
    private readonly Label _statusLabel;

    public event Action<string>? OnSearchChanged;
    public event Action? OnFilterChanged;
    public event Action? OnRebuildRequested;

    public bool FilterUserOnly { get; set; } = false;
    public bool IncludeSystemPrimitives { get; set; } = false;
    public bool FilterComponentsOnly { get; set; } = false;
    public bool FilterRazorOnly { get; set; } = false;

    public CanvasTopHud( Widget parent ) : base( parent )
    {
        FocusMode = FocusMode.Click;
        Cursor = CursorShape.Arrow;

        SetStyles( @"
            background-color: rgba( 18, 20, 26, 0.92 );
            border: 1px solid rgba( 255, 255, 255, 0.12 );
            border-radius: 8px;
            padding: 4px 8px;
        " );

        Layout = Layout.Row();
        Layout.Margin = 2;
        Layout.Spacing = 6;
        FixedHeight = 36;

        // 1. Search Box
        _searchBox = Layout.Add( new LineEdit( this ) );
        _searchBox.PlaceholderText = "Search architecture...";
        _searchBox.ClearButtonEnabled = true;
        _searchBox.FixedWidth = 180;
        _searchBox.TextEdited += text => OnSearchChanged?.Invoke( text );

        Layout.AddSpacingCell( 4 );

        // 2. Scope & Category Pills
        _btnUser = CreatePill( "User Code", false, () =>
        {
            FilterUserOnly = !FilterUserOnly;
            UpdatePillStates();
            OnFilterChanged?.Invoke();
        } );

        _btnSystem = CreatePill( "System (.NET)", false, () =>
        {
            IncludeSystemPrimitives = !IncludeSystemPrimitives;
            UpdatePillStates();
            OnFilterChanged?.Invoke();
        } );

        Layout.AddSpacingCell( 4 );

        _btnComponents = CreatePill( "Components", false, () =>
        {
            FilterComponentsOnly = !FilterComponentsOnly;
            if ( FilterComponentsOnly ) FilterRazorOnly = false;
            UpdatePillStates();
            OnFilterChanged?.Invoke();
        } );

        _btnRazor = CreatePill( "Razor UI", false, () =>
        {
            FilterRazorOnly = !FilterRazorOnly;
            if ( FilterRazorOnly ) FilterComponentsOnly = false;
            UpdatePillStates();
            OnFilterChanged?.Invoke();
        } );

        // 3. Rebuild Button
        var rebuildBtn = Layout.Add( new Button( "refresh", this ) );
        rebuildBtn.ToolTip = "Rebuild Graph";
        rebuildBtn.Clicked = () => OnRebuildRequested?.Invoke();
        rebuildBtn.FixedWidth = 28;

        // 4. Status Counter
        Layout.AddSpacingCell( 4 );
        _statusLabel = Layout.Add( new Label( "0 nodes", this ) );
        _statusLabel.SetStyles( "color: #8b949e; font-size: 11px;" );

        UpdatePillStates();
    }

    private Button CreatePill( string text, bool active, Action onClick )
    {
        var btn = Layout.Add( new Button( text, this ) );
        btn.Clicked = onClick;
        ApplyPillStyle( btn, active );
        return btn;
    }

    private void UpdatePillStates()
    {
        ApplyPillStyle( _btnUser, FilterUserOnly );
        ApplyPillStyle( _btnSystem, IncludeSystemPrimitives );
        ApplyPillStyle( _btnComponents, FilterComponentsOnly );
        ApplyPillStyle( _btnRazor, FilterRazorOnly );
    }

    private static void ApplyPillStyle( Button btn, bool active )
    {
        if ( active )
        {
            btn.SetStyles( @"
                background-color: rgba( 79, 172, 254, 0.30 );
                border: 1px solid #4facfe;
                border-radius: 12px;
                color: #ffffff;
                font-size: 11px;
                padding: 2px 10px;
            " );
        }
        else
        {
            btn.SetStyles( @"
                background-color: rgba( 255, 255, 255, 0.05 );
                border: 1px solid rgba( 255, 255, 255, 0.10 );
                border-radius: 12px;
                color: #9aa0a6;
                font-size: 11px;
                padding: 2px 10px;
            " );
        }
    }

    public void UpdateStatus( int visibleNodes, int totalNodes, int edges )
    {
        _statusLabel.Text = $"{visibleNodes:N0} nodes / {edges:N0} edges";
    }

    protected override void OnMousePress( MouseEvent e ) => e.Accepted = true;
    protected override void OnMouseMove( MouseEvent e ) => e.Accepted = true;
    protected override void OnMouseWheel( WheelEvent e ) => e.Accepted = true;
}