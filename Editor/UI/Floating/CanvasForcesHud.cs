#nullable enable
using System;
using ArchitectureVisualizer.UI;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.Floating;

/// <summary>
/// Floating semi-transparent HUD menu for Display settings, 2D/3D toggling, and live Physics tuning.
/// </summary>
public sealed class CanvasForcesHud : Widget
{
    private readonly CanvasWidget _canvas;
    private readonly Widget _contentPanel;
    private bool _isOpen = false;
    private Button? _modeBtn;

    public CanvasForcesHud( CanvasWidget canvas ) : base( canvas )
    {
        _canvas = canvas;
        FocusMode = FocusMode.Click;
        Cursor = CursorShape.Arrow;

        SetStyles( @"
            background-color: rgba( 18, 20, 26, 0.94 );
            border: 1px solid rgba( 255, 255, 255, 0.12 );
            border-radius: 8px;
            padding: 4px;
        " );

        Layout = Layout.Column();
        Layout.Margin = 2;
        Layout.Spacing = 4;

        // Toggle Button
        var toggleBtn = Layout.Add( new Button( "Settings ⚙️", "tune", this ) );
        toggleBtn.Clicked = ToggleMenu;
        toggleBtn.FixedHeight = 28;

        // Collapsible Panel
        _contentPanel = Layout.Add( new Widget( this ) );
        _contentPanel.Layout = Layout.Column();
        _contentPanel.Layout.Spacing = 6;
        _contentPanel.Hidden = true;
        _contentPanel.FixedWidth = 220;

        BuildControls();
        AdjustSize();
        UpdatePosition();
    }

    private void ToggleMenu()
    {
        _isOpen = !_isOpen;
        _contentPanel.Hidden = !_isOpen;
        AdjustSize();
        UpdatePosition();
        Update();
    }

    public void UpdatePosition()
    {
        if ( Parent == null ) return;
        Position = new Vector2( Parent.Width - Width - 14f, 14f );
    }

    private void BuildControls()
    {
        var theme = _canvas.Theme;
        var p = _canvas.Physics;

        // ================= VIEWPORT MODE =================
        var viewHeader = _contentPanel.Layout.Add( new Label( "CAMERA VIEWPORT", _contentPanel ) );
        viewHeader.SetStyles( "color: #58a6ff; font-weight: bold; font-size: 10px; margin-top: 2px;" );

        _modeBtn = _contentPanel.Layout.Add( new Button( "Switch to 3D Orbit 🪐", "3d_rotation", _contentPanel ) );
        _modeBtn.FixedHeight = 24;
        _modeBtn.Clicked = () =>
        {
            _canvas.CameraController.ToggleMode();
            _modeBtn.Text = _canvas.CameraController.Is3DMode ? "Switch to 2D Ortho 📄" : "Switch to 3D Orbit 🪐";
            _canvas.SyncGpuBuffers();
            _canvas.Update();
        };

        // ================= DISPLAY SECTION =================
        var displayHeader = _contentPanel.Layout.Add( new Label( "DISPLAY SCALE", _contentPanel ) );
        displayHeader.SetStyles( "color: #58a6ff; font-weight: bold; font-size: 10px; margin-top: 6px;" );

        // 1. Node Size Scale
        AddSlider( "Node Size", 0.4f, 8.0f, theme.NodeSizeScale, val =>
        {
            theme.NodeSizeScale = val;
            _canvas.SyncGpuBuffers();
            _canvas.Physics.Reheat( 0.40f );
            _canvas.Update();
        } );

        // 2. Link Thickness Scale
        AddSlider( "Link Thickness", 0.2f, 4.0f, theme.LinkThicknessScale, val =>
        {
            theme.LinkThicknessScale = val;
            _canvas.SyncGpuBuffers();
            _canvas.Update();
        } );

        // ================= TEXT LABELS SECTION =================
        var textHeader = _contentPanel.Layout.Add( new Label( "TEXT LABELS", _contentPanel ) );
        textHeader.SetStyles( "color: #e3b341; font-weight: bold; font-size: 10px; margin-top: 6px;" );

        // Text Size Slider
        AddSlider( "Text Size", 0.4f, 3.0f, theme.TextSizeScale, val =>
        {
            theme.TextSizeScale = val;
            _canvas.SyncGpuBuffers();
            _canvas.Update();
        } );

        // Mode Button (Cycles: Smart LOD -> All Labels -> Hover Only)
        string GetModeButtonText() => theme.LabelMode switch
        {
            ArchitectureVisualizer.UI.CanvasEngine.Models.TextLabelMode.AlwaysShowAll => "Mode: Always Show All 👁️",
            ArchitectureVisualizer.UI.CanvasEngine.Models.TextLabelMode.HoverOnly => "Mode: Hover & Select Only 🎯",
            _ => "Mode: Smart LOD 🧠"
        };

        var labelModeBtn = _contentPanel.Layout.Add( new Button( GetModeButtonText(), "label", _contentPanel ) );
        labelModeBtn.FixedHeight = 24;
        labelModeBtn.Clicked = () =>
        {
            theme.LabelMode = theme.LabelMode switch
            {
                ArchitectureVisualizer.UI.CanvasEngine.Models.TextLabelMode.SmartLOD => ArchitectureVisualizer.UI.CanvasEngine.Models.TextLabelMode.AlwaysShowAll,
                ArchitectureVisualizer.UI.CanvasEngine.Models.TextLabelMode.AlwaysShowAll => ArchitectureVisualizer.UI.CanvasEngine.Models.TextLabelMode.HoverOnly,
                _ => ArchitectureVisualizer.UI.CanvasEngine.Models.TextLabelMode.SmartLOD
            };

            labelModeBtn.Text = GetModeButtonText();
            _canvas.SyncGpuBuffers();
            _canvas.Update();
        };

        // ================= FORCES SECTION =================
        var forcesHeader = _contentPanel.Layout.Add( new Label( "PHYSICS FORCES", _contentPanel ) );
        forcesHeader.SetStyles( "color: #7ee787; font-weight: bold; font-size: 10px; margin-top: 6px;" );

        AddSlider( "Link Distance", 30f, 600f, p.LinkDistanceSetting, val =>
        {
            p.LinkDistanceSetting = val;
            _canvas.Physics.Reheat( 0.35f );
            _canvas.Update();
        } );

        AddSlider( "Link Force", 0.05f, 3.0f, p.LinkForceSetting, val =>
        {
            p.LinkForceSetting = val;
            _canvas.Physics.Reheat( 0.35f );
            _canvas.Update();
        } );

        AddSlider( "Repel Force", 0.0f, 35.0f, p.RepulsionConstant, val =>
        {
            p.RepulsionConstant = val;
            _canvas.Physics.Reheat( 0.60f );
            _canvas.Update();
        } );

        AddSlider( "Center Force", 0.0f, 1.5f, p.CenterForceSetting, val =>
        {
            p.CenterForceSetting = val;
            _canvas.Physics.Reheat( 0.35f );
            _canvas.Update();
        } );

        // Freeze during play toggle
        var playCheck = _contentPanel.Layout.Add( new Checkbox( "Freeze During Play", _contentPanel ) );
        playCheck.Value = p.PauseDuringPlay;
        playCheck.StateChanged += _ => p.PauseDuringPlay = playCheck.Value;

        // Reheat Button
        var reheatBtn = _contentPanel.Layout.Add( new Button( "Reheat Physics 🔥", "local_fire_department", _contentPanel ) );
        reheatBtn.Clicked = () =>
        {
            p.Reheat( 1.0f );
            _canvas.Update();
        };
    }

    private void AddSlider( string name, float min, float max, float currentVal, Action<float> onValueChanged )
    {
        var row = _contentPanel.Layout.Add( new Widget( _contentPanel ) );
        row.Layout = Layout.Column();
        row.Layout.Spacing = 2;

        var label = row.Layout.Add( new Label( $"{name}: {currentVal:F2}", row ) );
        label.SetStyles( "color: #c9d1d9; font-size: 10px;" );

        var slider = row.Layout.Add( new FloatSlider( row ) );
        slider.Minimum = min;
        slider.Maximum = max;
        slider.Value = currentVal;
        slider.OnValueEdited += () =>
        {
            label.Text = $"{name}: {slider.Value:F2}";
            onValueChanged( slider.Value );
        };
    }

    protected override void OnMousePress( MouseEvent e ) => e.Accepted = true;
    protected override void OnMouseMove( MouseEvent e ) => e.Accepted = true;
    protected override void OnMouseWheel( WheelEvent e ) => e.Accepted = true;
}