using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The biome survey decoded once on the CPU. Every scatter layer reads the same copy, and it
/// outlives scene reloads, so a restart does not read back and decompress the texture again.</summary>
internal sealed class BiomeMap {

    private static BiomeMap _loaded;

    private readonly string _path;
    private readonly byte[] _pixels;
    private readonly int _width;
    private readonly int _height;

    private BiomeMap(Texture2D texture) {

        _path = texture.ResourcePath;
        using Image image = texture.GetImage();
        if (image.IsCompressed()) {

            image.Decompress();

        }
        image.Convert(Image.Format.Rgba8);
        _width = image.GetWidth();
        _height = image.GetHeight();
        _pixels = image.GetData();

    }

    public static BiomeMap Of(Texture2D texture) {

        BiomeMap loaded = _loaded;
        if (loaded != null && !string.IsNullOrEmpty(loaded._path) && loaded._path == texture.ResourcePath) {

            return loaded;

        }
        return _loaded = new BiomeMap(texture);

    }

    public Color Cover(Vector3d direction) {

        double x = (Math.Atan2(direction.Y, direction.X) / (Math.PI * 2.0) + 0.5) * _width - 0.5;
        double y = (0.5 - Math.Asin(direction.Z) / Math.PI) * _height - 0.5;
        int left = (int)Math.Floor(x);
        int top = (int)Math.Floor(y);
        Color upper = Pixel(left, top).Lerp(Pixel(left + 1, top), (float)(x - left));
        Color lower = Pixel(left, top + 1).Lerp(Pixel(left + 1, top + 1), (float)(x - left));
        return upper.Lerp(lower, (float)(y - top));

    }

    private Color Pixel(int x, int y) {

        int index = (Math.Clamp(y, 0, _height - 1) * _width + ((x % _width) + _width) % _width) * 4;
        return new Color(_pixels[index] / 255.0f, _pixels[index + 1] / 255.0f,
            _pixels[index + 2] / 255.0f, _pixels[index + 3] / 255.0f);

    }

}
