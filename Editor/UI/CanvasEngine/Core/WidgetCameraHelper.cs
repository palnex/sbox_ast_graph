#nullable enable
using System;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Core;

public static class WidgetCameraHelper
{
    public static Matrix ComputeViewMatrix( Vector3 position, Rotation rotation )
    {
        Vector3 r = rotation.Right;
        Vector3 u = rotation.Up;
        Vector3 f = rotation.Forward;

        return new Matrix(
             r.x, r.y, r.z, -Vector3.Dot( r, position ),
             u.x, u.y, u.z, -Vector3.Dot( u, position ),
            -f.x, -f.y, -f.z, Vector3.Dot( f, position ),
             0f, 0f, 0f, 1f
        );
    }

    public static Matrix ComputeProjectionMatrix( CameraComponent camera, float aspectRatio )
    {
        if ( camera.Orthographic )
        {
            float orthoH = Math.Max( 1f, camera.OrthographicHeight );
            float orthoW = orthoH * aspectRatio;
            float near = camera.ZNear;
            float far = camera.ZFar;
            float range = 1.0f / (near - far);

            return new Matrix(
                2.0f / orthoW, 0f, 0f, 0f,
                0f, 2.0f / orthoH, 0f, 0f,
                0f, 0f, range, near * range,
                0f, 0f, 0f, 1f
            );
        }
        else
        {
            float fovRad = camera.FieldOfView * (MathF.PI / 180.0f);
            float tanHalfFov = MathF.Tan( fovRad * 0.5f );
            float near = camera.ZNear;
            float far = camera.ZFar;
            float range = far / (near - far);

            return new Matrix(
                1.0f / (aspectRatio * tanHalfFov), 0f, 0f, 0f,
                0f, 1.0f / tanHalfFov, 0f, 0f,
                0f, 0f, range, near * range,
                0f, 0f, -1.0f, 0f
            );
        }
    }

    public static Matrix ComputeViewProjection( CameraComponent camera, float aspectRatio )
    {
        var view = ComputeViewMatrix( camera.WorldPosition, camera.WorldRotation );
        var proj = ComputeProjectionMatrix( camera, aspectRatio );

        // Transpose for Slang HLSL mul(matrix, vector) column-major convention
        var viewProj = proj * view;
        return viewProj.Transpose();
    }
}