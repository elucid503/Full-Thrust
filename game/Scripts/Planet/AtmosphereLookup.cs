using System;

using Godot;

namespace FullThrust.Game;

public static class AtmosphereLookup {

    public const int Width = 256;
    public const int Height = 128;
    private static (double, double, double, double, double, double) _key;
    private static ImageTexture _cached;

    public static ImageTexture Build(double radius, double top, double rayleigh, double mie, double ozone, double ozoneWidth) {

        var key = (radius, top, rayleigh, mie, ozone, ozoneWidth);
        if (GodotObject.IsInstanceValid(_cached) && _key == key) { return _cached; }

        float[] pixels = new float[Width * Height * 3];

        for (int row = 0; row < Height; row++) {

            double height = Math.Pow((double)row / (Height - 1), 2.0) * (top - radius);

            for (int column = 0; column < Width; column++) {

                double signed = 2.0 * column / (Width - 1) - 1.0;
                double cosine = signed * Math.Abs(signed);
                var depth = Integrate(radius, top, height, cosine, rayleigh, mie, ozone, ozoneWidth, 64);
                int index = (row * Width + column) * 3;
                pixels[index] = (float)(depth.X / rayleigh);
                pixels[index + 1] = (float)(depth.Y / mie);
                pixels[index + 2] = (float)(depth.Z / ozoneWidth);

            }

        }

        byte[] data = new byte[pixels.Length * sizeof(float)];
        Buffer.BlockCopy(pixels, 0, data, 0, data.Length);
        using Image image = Image.CreateFromData(Width, Height, false, Image.Format.Rgbf, data);
        _key = key;
        _cached = ImageTexture.CreateFromImage(image);
        return _cached;

    }

    public static (double X, double Y, double Z) Integrate(double radius, double top, double height,
        double cosine, double rayleigh, double mie, double ozone, double ozoneWidth, int samples) {

        double r = radius + height;
        double b = r * cosine;
        double length = -b + Math.Sqrt(Math.Max(0.0, b * b + (top - r) * (top + r)));
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;

        for (int sample = 0; sample < samples; sample++) {

            double low = Math.Pow((double)sample / samples, 2.0);
            double high = Math.Pow((double)(sample + 1) / samples, 2.0);
            double at = length * (low + high) * 0.5;
            double h = Math.Max(Math.Sqrt(r * r + at * (at + 2.0 * b)) - radius, 0.0);
            double stride = length * (high - low);
            x += Math.Exp(-h / rayleigh) * stride;
            y += Math.Exp(-h / mie) * stride;
            z += Math.Max(1.0 - Math.Abs(h - ozone) / ozoneWidth, 0.0) * stride;

        }

        return (x, y, z);

    }

}
