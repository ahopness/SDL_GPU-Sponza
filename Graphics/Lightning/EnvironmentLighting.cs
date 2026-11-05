using System.Numerics;

namespace SponzaTest.Graphics.Lightning;

public static class EnvironmentLighting
{
    public static Vector4[] ProjectDiffuse(CpuTexture environment)
    {
        int width = environment.Width, height = environment.Height;
        var pixels = environment.Rgba32FloatData;
        if (width <= 0 || height <= 0 || pixels == null || pixels.Length != checked(width * height * 4))
            throw new ArgumentException("Expected an HDR environment with RGBA float pixels.", nameof(environment));

        var sums = new double[9, 3];
        Span<double> basis = stackalloc double[9];
        for (int y = 0; y < height; y++)
        {
            double theta = Math.PI * (y + 0.5) / height;
            double dy = Math.Cos(theta), radius = Math.Sin(theta);
            if (environment.FlipY) dy = -dy;
            double weight = 2 * Math.PI / width *
                (Math.Cos(Math.PI * y / height) - Math.Cos(Math.PI * (y + 1) / height));
            for (int x = 0; x < width; x++)
            {
                double phi = 2 * Math.PI * ((x + 0.5) / width - 0.5);
                double dx = radius * Math.Cos(phi), dz = radius * Math.Sin(phi);
                basis[0] = 0.2820947918;
                basis[1] = 0.4886025119 * dy;
                basis[2] = 0.4886025119 * dz;
                basis[3] = 0.4886025119 * dx;
                basis[4] = 1.0925484306 * dx * dy;
                basis[5] = 1.0925484306 * dy * dz;
                basis[6] = 0.3153915653 * (3 * dz * dz - 1);
                basis[7] = 1.0925484306 * dx * dz;
                basis[8] = 0.5462742153 * (dx * dx - dy * dy);
                int pixel = (y * width + x) * 4;
                for (int i = 0; i < 9; i++)
                    for (int c = 0; c < 3; c++)
                        sums[i, c] += pixels[pixel + c] * basis[i] * weight;
            }
        }
        var coefficients = new Vector4[9];
        for (int i = 0; i < 9; i++)
        {
            double convolution = i == 0 ? 1 : i < 4 ? 2.0 / 3 : 0.25;
            coefficients[i] = new((float)(sums[i, 0] * convolution),
                (float)(sums[i, 1] * convolution), (float)(sums[i, 2] * convolution), 0);
        }
        return coefficients;
    }
}
