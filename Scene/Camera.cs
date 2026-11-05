using System.Numerics;
using SDL3;

namespace SponzaTest.Scene;

public class Camera
{
    public Vector3 Position { get; set; } = new(2f, 5f, 5f);
    public Vector3 Target { get; set; } = Vector3.Zero;
    public Vector3 Up { get; set; } = Vector3.UnitY;

    public float FovDegrees { get; set; } = 60f;
    public float Aspect { get; set; } = 16f / 9f;
    public float Near { get; set; } = 0.1f;
    public float Far { get; set; } = 2000f;

    public Matrix4x4 ViewMatrix { get; private set; } = Matrix4x4.Identity;
    public Matrix4x4 CameraWorldMatrix { get; private set; } = Matrix4x4.Identity;
    public Matrix4x4 ProjectionMatrix { get; private set; } = Matrix4x4.Identity;
    public Matrix4x4 ProjectionMatrixInverse { get; private set; } = Matrix4x4.Identity;
    public Matrix4x4 ViewProjectionMatrix { get; private set; } = Matrix4x4.Identity;

    public void UpdateMatrices()
    {
        ViewMatrix = Matrix4x4.CreateLookAt(Position, Target, Up);
        Matrix4x4.Invert(ViewMatrix, out Matrix4x4 camWorld);
        CameraWorldMatrix = camWorld;

        ProjectionMatrix = CreatePerspectiveFieldOfViewZeroToOne(
            FovDegrees * (MathF.PI / 180f),
            MathF.Max(0.01f, Aspect),
            Near,
            Far
        );

        Matrix4x4.Invert(ProjectionMatrix, out Matrix4x4 projInv);
        ProjectionMatrixInverse = projInv;

        ViewProjectionMatrix = ViewMatrix * ProjectionMatrix;
    }

    public static Matrix4x4 CreatePerspectiveFieldOfViewZeroToOne(float fovRadians, float aspect, float near, float far)
    {
        float yScale = 1.0f / MathF.Tan(fovRadians * 0.5f);
        float xScale = yScale / aspect;
        float farOverRange = far / (near - far);

        return new Matrix4x4(
            xScale, 0f, 0f, 0f,
            0f, yScale, 0f, 0f,
            0f, 0f, farOverRange, -1f,
            0f, 0f, near * farOverRange, 0f
        );
    }

    public static Matrix4x4 CreateOrthographicOffCenterZeroToOne(
        float left, float right, float bottom, float top, float near, float far)
    {
        float invRL = 1.0f / (right - left);
        float invTB = 1.0f / (top - bottom);
        float invNF = 1.0f / (near - far);

        return new Matrix4x4(
            2.0f * invRL, 0f, 0f, 0f,
            0f, 2.0f * invTB, 0f, 0f,
            0f, 0f, invNF, 0f,
            -(right + left) * invRL, -(top + bottom) * invTB, near * invNF, 1f
        );
    }
}

public class OrbitControls
{
    private readonly Camera _camera;
    private Vector2 _lastMousePos;
    private bool _isRotating;
    private bool _isPanning;

    public bool SuppressInput { get; set; }

    public OrbitControls(Camera camera)
    {
        _camera = camera;
    }

    public void HandleEvent(in SDL.Event e)
    {
        switch ((SDL.EventType)e.Type)
        {
            case SDL.EventType.WindowFocusLost:
                _isRotating = _isPanning = false;
                break;
            case SDL.EventType.MouseButtonDown:
                if (SuppressInput) break;
                _lastMousePos = new(e.Button.X, e.Button.Y);
                if (e.Button.Button == 1) _isRotating = true;
                if (e.Button.Button is 2 or 3) _isPanning = true;
                break;
            case SDL.EventType.MouseButtonUp:
                if (e.Button.Button == 1) _isRotating = false;
                if (e.Button.Button is 2 or 3) _isPanning = false;
                break;
            case SDL.EventType.MouseMotion:
                OnMouseMove(new(e.Motion.X, e.Motion.Y));
                break;
            case SDL.EventType.MouseWheel:
                OnScroll(e.Wheel.Y);
                break;
        }
    }

    private void OnMouseMove(Vector2 pos)
    {
        if (SuppressInput)
        {
            _isRotating = _isPanning = false;
            return;
        }

        Vector2 delta = pos - _lastMousePos;
        _lastMousePos = pos;

        if (_isRotating)
        {
            Vector3 offset = _camera.Position - _camera.Target;
            float radius = offset.Length();
            if (radius < 1e-4f) radius = 0.1f;

            float theta = MathF.Atan2(offset.X, offset.Z);
            float phi = MathF.Acos(Math.Clamp(offset.Y / radius, -1f, 1f));

            const float rotSpeed = 0.005f;
            theta -= delta.X * rotSpeed;
            phi = Math.Clamp(phi - delta.Y * rotSpeed, 0.02f, MathF.PI - 0.02f);

            offset.X = radius * MathF.Sin(phi) * MathF.Sin(theta);
            offset.Y = radius * MathF.Cos(phi);
            offset.Z = radius * MathF.Sin(phi) * MathF.Cos(theta);

            _camera.Position = _camera.Target + offset;
        }
        else if (_isPanning)
        {
            Vector3 forward = Vector3.Normalize(_camera.Target - _camera.Position);
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));

            float dist = (_camera.Position - _camera.Target).Length();
            float panSpeed = dist * 0.0015f;
            Vector3 pan = (-right * delta.X + up * delta.Y) * panSpeed;

            _camera.Position += pan;
            _camera.Target += pan;
        }
    }

    private void OnScroll(float scrollY)
    {
        if (SuppressInput) return;
        Vector3 offset = _camera.Position - _camera.Target;
        float dist = offset.Length();
        float factor = MathF.Pow(0.9f, scrollY);
        dist = Math.Clamp(dist * factor, 0.2f, 1000f);
        _camera.Position = _camera.Target + (offset.LengthSquared() > 1e-8f ? Vector3.Normalize(offset) : Vector3.UnitZ) * dist;
    }
}
