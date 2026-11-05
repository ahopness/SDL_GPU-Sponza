using System.Numerics;

namespace SponzaTest.Graphics;

public readonly record struct BoundingBox(Vector3 Min, Vector3 Max)
{
    public static BoundingBox FromVertices(ReadOnlySpan<MeshVertex> vertices)
    {
        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);
        foreach (var vertex in vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }
        return new BoundingBox(min, max);
    }

    public Vector3 GetCorner(int index) => new(
        (index & 1) == 0 ? Min.X : Max.X,
        (index & 2) == 0 ? Min.Y : Max.Y,
        (index & 4) == 0 ? Min.Z : Max.Z);

    // System.Numerics uses row vectors; SDL GPU clip depth is 0 <= z <= w.
    // Test local bounds against transformed clip planes to support perspective,
    // rotation, nonuniform scale, and objects crossing the camera's near plane.
    public bool IntersectsClipVolume(Matrix4x4 transform)
    {
        Vector4 x = new(transform.M11, transform.M21, transform.M31, transform.M41);
        Vector4 y = new(transform.M12, transform.M22, transform.M32, transform.M42);
        Vector4 z = new(transform.M13, transform.M23, transform.M33, transform.M43);
        Vector4 w = new(transform.M14, transform.M24, transform.M34, transform.M44);
        return !IsOutside(w + x) && !IsOutside(w - x)
            && !IsOutside(w + y) && !IsOutside(w - y)
            && !IsOutside(z) && !IsOutside(w - z);
    }

    private bool IsOutside(Vector4 plane)
    {
        Vector3 support = new(
            plane.X >= 0 ? Max.X : Min.X,
            plane.Y >= 0 ? Max.Y : Min.Y,
            plane.Z >= 0 ? Max.Z : Min.Z);
        // Keep boundary cases to avoid flickering from floating-point rounding.
        float tolerance = 1e-5f * (1f + MathF.Abs(plane.W));
        return Vector3.Dot(new Vector3(plane.X, plane.Y, plane.Z), support) + plane.W < -tolerance;
    }
}
