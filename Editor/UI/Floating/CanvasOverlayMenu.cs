using System;
using ArchitectureVisualizer.UI.CanvasEngine.Widgets;
using Editor;
using Sandbox;

namespace ArchitectureVisualizer.UI.Floating;

/// <summary>
/// Floating semi-transparent HUD overlay on the canvas for live physics and display tuning.
/// </summary>
public sealed class CanvasOverlayMenu : Widget
{
    private readonly CanvasWidget _canvas;
    private readonly Widget _contentPanel;
    private bool _isOpen = false;

    public CanvasOverlayMenu( CanvasWidget canvas ) : base( canvas )
    {
        _canvas = canvas;
        Position = new Vector2( 12, 12 );

        // Dark translucent glass styling
        SetStyles( "background-color: rgba( 18, 20, 26, 0.94 ); border: 1px solid #383e4d; border-radius: 6px; padding: 6px;" );

        Layout = Layout.Column();
        Layout.Margin = 4;
        Layout.Spacing = 4;

        // --- Header Toggle Button ---
        var toggleBtn = Layout.Add( new Button( "Forces ⚙️", "tune", this ) );
        toggleBtn.Clicked = ToggleMenu;
        toggleBtn.FixedHeight = 26;

        // --- Collapsible Settings Panel ---
        _contentPanel = Layout.Add( new Widget( this ) );
        _contentPanel.Layout = Layout.Column();
        _contentPanel.Layout.Spacing = 6;
        _contentPanel.Hidden = true; // s&box visibility toggle
        _contentPanel.FixedWidth = 220;

        BuildSliders();
        AdjustSize();
    }

    private void ToggleMenu()
    {
        _isOpen = !_isOpen;
        _contentPanel.Hidden = !_isOpen;
        AdjustSize();
        Update();
    }

    private void BuildSliders()
    {
        var p = _canvas.Physics;

        // 1. Link Distance (L0)
        AddSliderRow( "Link Distance", 30f, 600f, p.LinkDistanceSetting, val =>
        {
            p.LinkDistanceSetting = val;
            OnSettingChanged();
        } );

        // 2. Link Strength
        AddSliderRow( "Link Strength", 0.05f, 2.0f, p.LinkForceSetting, val =>
        {
            p.LinkForceSetting = val;
            OnSettingChanged();
        } );

        // 3. Repel Force (Barnes-Hut)
        AddSliderRow( "Repel Force", 100f, 5000f, p.RepulsionConstant, val =>
        {
            p.RepulsionConstant = val;
            OnSettingChanged();
        } );

        // 4. Center Force
        AddSliderRow( "Center Force", 0.0f, 1.5f, p.CenterForceSetting, val =>
        {
            p.CenterForceSetting = val;
            OnSettingChanged();
        } );

        // 5. Max Repel Distance
        AddSliderRow( "Repel Radius", 100f, 1500f, p.RepulsionMaxDist, val =>
        {
            p.RepulsionMaxDist = val;
            OnSettingChanged();
        } );

        // Quick Reheat Button
        var reheatBtn = _contentPanel.Layout.Add( new Button( "Reheat Physics 🔥", "local_fire_department", _contentPanel ) );
        reheatBtn.Clicked = () =>
        {
            p.Reheat( 1.0f );
            _canvas.Update();
        };
    }

    private void AddSliderRow( string name, float min, float max, float currentVal, Action<float> onValueChanged )
    {
        var row = _contentPanel.Layout.Add( new Widget( _contentPanel ) );
        row.Layout = Layout.Column();
        row.Layout.Spacing = 2;

        var label = row.Layout.Add( new Label( $"{name}: {currentVal:F0}", row ) );

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

    private void OnSettingChanged()
    {
        _canvas.Physics.Reheat( 0.35f );
        _canvas.Update();
    }
}