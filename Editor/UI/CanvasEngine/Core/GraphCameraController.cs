#nullable enable
using System;
using Sandbox;

namespace ArchitectureVisualizer.UI.CanvasEngine.Core;

/// <summary>
/// Flexible 2D Orthographic and 3D Orbit Camera Controller for CameraComponent.
/// </summary>
public sealed class GraphCameraController
{
    private readonly CameraComponent _camera;
    private readonly GameObject _cameraObject;

    public bool Is3DMode { get; private set; } = false;

    private float _orthoSize = 2500.0f;
    private float _orbitDistance = 2500.0f;
    private Angles _orbitAngles = new( 45, -45, 0 );
    private Vector3 _focalPoint = Vector3.Zero;

    private Vector3 _targetFocalPoint = Vector3.Zero;
    private float _targetOrthoSize = 2500.0f;
    private float _targetOrbitDistance = 2500.0f;
    private bool _isAnimating = false;

    public bool IsAnimating => _isAnimating;
    public CameraComponent Camera => _camera;
    public GameObject CameraObject => _cameraObject;
    public Vector3 FocalPoint => _focalPoint;
    public float OrthoSize => _orthoSize;

    public GraphCameraController( CameraComponent camera, GameObject cameraObject )
    {
        _camera = camera;
        _cameraObject = cameraObject;
        _targetFocalPoint = _focalPoint;
        _targetOrthoSize = _orthoSize;
        _targetOrbitDistance = _orbitDistance;

        Set2DMode();
    }

    public void Set2DMode()
    {
        Is3DMode = false;
        _camera.Orthographic = true;
        _camera.OrthographicHeight = _orthoSize;
        _cameraObject.WorldRotation = Rotation.From( new Angles( 90, -90, 0 ) );
        _cameraObject.WorldPosition = _focalPoint + Vector3.Up * 10000.0f;
    }

    public void Zoom( float deltaWheel )
    {
        _isAnimating = false;
        float factor = deltaWheel > 0 ? 0.85f : 1.15f;

        if ( !Is3DMode )
        {
            _orthoSize = Math.Clamp( _orthoSize * factor, 200.0f, 60000.0f );
            _targetOrthoSize = _orthoSize;
            _camera.OrthographicHeight = _orthoSize;
        }
        else
        {
            _orbitDistance = Math.Clamp( _orbitDistance * factor, 100.0f, 60000.0f );
            _targetOrbitDistance = _orbitDistance;
            UpdateOrbitTransform();
        }
    }

    public void UpdateAnimation( float dt )
    {
        if ( !_isAnimating ) return;

        float t = 1.0f - MathF.Exp( -14.0f * dt );

        _focalPoint = Vector3.Lerp( _focalPoint, _targetFocalPoint, t );
        _orthoSize = MathX.Lerp( _orthoSize, _targetOrthoSize, t );
        _orbitDistance = MathX.Lerp( _orbitDistance, _targetOrbitDistance, t );

        if ( (_focalPoint - _targetFocalPoint).Length < 0.5f && MathF.Abs( _orthoSize - _targetOrthoSize ) < 1.0f )
        {
            _focalPoint = _targetFocalPoint;
            _orthoSize = _targetOrthoSize;
            _orbitDistance = _targetOrbitDistance;
            _isAnimating = false;
        }

        if ( !Is3DMode )
        {
            _camera.OrthographicHeight = _orthoSize;
            _cameraObject.WorldPosition = _focalPoint + Vector3.Up * 10000.0f;
        }
        else
        {
            UpdateOrbitTransform();
        }
    }

    public void Set3DMode()
    {
        Is3DMode = true;
        _camera.Orthographic = false;
        _camera.FieldOfView = 60.0f;
        UpdateOrbitTransform();
    }

    public void ToggleMode()
    {
        if ( Is3DMode ) Set2DMode();
        else Set3DMode();
    }

    public void Pan( Vector2 screenDeltaPixels, Vector2 viewportSize )
    {
        _isAnimating = false;

        if ( !Is3DMode )
        {
            float unitsPerPixel = _orthoSize / MathF.Max( 1f, viewportSize.y );
            _focalPoint += new Vector3( screenDeltaPixels.x * unitsPerPixel, -screenDeltaPixels.y * unitsPerPixel, 0 );
            _targetFocalPoint = _focalPoint;
            _cameraObject.WorldPosition = _focalPoint + Vector3.Up * 10000.0f;
        }
        else
        {
            var right = _cameraObject.WorldRotation.Right;
            var up = _cameraObject.WorldRotation.Up;
            float factor = (_orbitDistance / MathF.Max( 1f, viewportSize.y ));
            _focalPoint += (-right * screenDeltaPixels.x + up * screenDeltaPixels.y) * factor;
            _targetFocalPoint = _focalPoint;
            UpdateOrbitTransform();
        }
    }

    public void Orbit( Vector2 mouseDelta )
    {
        if ( !Is3DMode ) return;

        _isAnimating = false;
        _orbitAngles.pitch = Math.Clamp( _orbitAngles.pitch + mouseDelta.y * 0.35f, 5.0f, 89.0f );
        _orbitAngles.yaw -= mouseDelta.x * 0.35f;
        UpdateOrbitTransform();
    }


    public void AnimateTo( Vector3 targetWorldPos, float targetSize = 2000f )
    {
        _targetFocalPoint = targetWorldPos;
        _targetOrthoSize = Math.Clamp( targetSize, 200.0f, 60000.0f );
        _targetOrbitDistance = Math.Clamp( targetSize, 100.0f, 60000.0f );
        _isAnimating = true;
    }


    private void UpdateOrbitTransform()
    {
        _cameraObject.WorldRotation = Rotation.From( _orbitAngles );
        _cameraObject.WorldPosition = _focalPoint + _cameraObject.WorldRotation.Backward * _orbitDistance;
    }

    public Vector3? GetWorldPosOnPlane( Ray ray, float planeHeight = 0f )
    {
        Plane plane = new( new Vector3( 0, 0, planeHeight ), Vector3.Up );
        return plane.Trace( ray, twosided: true );
    }
}