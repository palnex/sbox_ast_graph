#nullable enable
using System.IO;
using Editor;
using Editor.Analysis.Internal.Navigation;
using ArchitectureVisualizer.UI.CanvasEngine.Models;
using Sandbox;

namespace ArchitectureVisualizer.UI.Interaction;

/// <summary>
/// Handles context menu generation for canvas nodes and background area.
/// </summary>
public static class CanvasContextMenu
{
    public static void Open( CanvasWidget canvas, int pickedNodeIndex )
    {
        var menu = new Menu( canvas );

        if ( pickedNodeIndex >= 0 && pickedNodeIndex < canvas.Registry.Count )
        {
            var payload = canvas.Registry.GetPayload( pickedNodeIndex );
            menu.AddHeading( payload.Title );

            if ( !string.IsNullOrEmpty( payload.FilePath ) )
            {
                menu.AddOption( "Open in Code Editor", "code", () =>
                {
                    CodeNavigator.OpenFile( payload.FilePath, payload.LineNumber );
                } );
            }

            menu.AddOption( "Focus Camera", "my_location", () => canvas.FocusOnNode( pickedNodeIndex, 1500f ) );

            bool isPinned = canvas.Registry.GetSpatialRef( pickedNodeIndex ).IsPinned;
            menu.AddOption( isPinned ? "Unpin Position" : "Pin in Place 📌", "push_pin", () =>
            {
                canvas.Registry.GetSpatialRef( pickedNodeIndex ).SetFlag( NodeFlags.Pinned, !isPinned );
                canvas.SyncGpuBuffers();
                canvas.Update();
            } );

            menu.AddSeparator();
            menu.AddOption( "Copy Type Name", "content_copy", () => EditorUtility.Clipboard.Copy( payload.Title ) );
            menu.AddOption( "Copy DocId", "fingerprint", () => EditorUtility.Clipboard.Copy( payload.Id ) );
        }
        else
        {
            menu.AddOption( canvas.CameraController.Is3DMode ? "Switch to 2D Ortho" : "Switch to 3D Orbit 🪐", "3d_rotation", () =>
            {
                canvas.CameraController.ToggleMode();
                canvas.SyncGpuBuffers();
                canvas.Update();
            } );
            menu.AddOption( "Fit All to Screen", "fit_screen", canvas.FitToScreen );
            menu.AddOption( "Reheat Physics 🔥", "bolt", () => { canvas.Physics.WakeUp(); canvas.Update(); } );
        }

        menu.OpenAtCursor();
    }
}