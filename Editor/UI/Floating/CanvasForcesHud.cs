#nullable enable
using System;
using ArchitectureVisualizer.UI.CanvasEngine.Widgets;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.Floating;

/// <summary>
/// Floating semi-transparent HUD menu for live physics tuning and simulation controls.
/// </summary>
public sealed class CanvasForcesHud : Widget
{
    private readonly CanvasWidget _canvas;
    private readonly Widget _contentPanel;
    private bool _isOpen = false;

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
        var toggleBtn = Layout.Add( new Button( "Forces ⚙️", "tune", this ) );
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

    /// <summary>
    /// Snaps the menu to the top-right corner of the parent canvas with proper margin.
    /// </summary>
    public void UpdatePosition()
    {
        if ( Parent == null ) return;
        float rightMargin = 14f;
        float topMargin = 14f;
        Position = new Vector2( Parent.Width - Width - rightMargin, topMargin );
    }

    private void BuildControls()
    {
        var p = _canvas.Physics;

        // 1. Link Distance
        AddSlider( "Link Distance", 30f, 600f, p.LinkDistanceSetting, val =>
        {
            p.LinkDistanceSetting = val;
            OnForceChanged();
        } );

        // 2. Link Strength
        AddSlider( "Link Strength", 0.05f, 2.0f, p.LinkForceSetting, val =>
        {
            p.LinkForceSetting = val;
            OnForceChanged();
        } );

        // 3. Repel Force (Barnes-Hut)
        AddSlider( "Repel Force", 100f, 5000f, p.RepulsionConstant, val =>
        {
            p.RepulsionConstant = val;
            OnForceChanged();
        } );

        // 4. Repel Radius
        AddSlider( "Repel Radius", 100f, 1500f, p.RepulsionMaxDist, val =>
        {
            p.RepulsionMaxDist = val;
            OnForceChanged();
        } );

        // 5. Center Force
        AddSlider( "Center Force", 0.0f, 1.5f, p.CenterForceSetting, val =>
        {
            p.CenterForceSetting = val;
            OnForceChanged();
        } );

        // 6. Freeze during play toggle
        var playCheck = _contentPanel.Layout.Add( new Checkbox( "Freeze During Play", _contentPanel ) );
        playCheck.Value = p.PauseDuringPlay;
        playCheck.StateChanged += _ => p.PauseDuringPlay = playCheck.Value;

        // 7. Reheat Button
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

        var label = row.Layout.Add( new Label( $"{name}: {currentVal:F0}", row ) );
        label.SetStyles( "color: #c9d1d9; font-size: 10px;" );

        var slider = row.Layout.Add( new FloatSlider( row ) );
        slider.Minimum = min;
        slider.Maximum = max;
        slider.Value = currentVal;
        slider.OnValueEdited += () =>
        {
            label.Text = $"{name}: {slider.Value:F1}";
            onValueChanged( slider.Value );
        };
    }

    private void OnForceChanged()
    {
        _canvas.Physics.Reheat( 0.35f );
        _canvas.Update();
    }

    protected override void OnMousePress( MouseEvent e ) => e.Accepted = true;
    protected override void OnMouseMove( MouseEvent e ) => e.Accepted = true;
    protected override void OnMouseWheel( WheelEvent e ) => e.Accepted = true;
}