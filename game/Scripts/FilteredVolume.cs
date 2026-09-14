using System;

using Godot;

namespace FullThrust.Game;

public static class FilteredVolume {

    public static ImageTexture3D Build(Texture3D source) {

        int size = source.GetWidth();
        if (size < 2 || (size & (size - 1)) != 0 || source.GetHeight() != size || source.GetDepth() != size || source.HasMipmaps()) {

            throw new ArgumentException("Cloud filtering requires a cubic power-of-two volume without mipmaps.", nameof(source));

        }
        Godot.Collections.Array<Image> images = source.GetData();
        byte[][] level = new byte[size][];
        for (int z = 0; z < size; z++) {

            images[z].Convert(Image.Format.L8);
            level[z] = images[z].GetData();

        }

        for (int width = size; width > 1;) {

            int next = width / 2;
            byte[][] reduced = new byte[next][];
            for (int z = 0; z < next; z++) {

                byte[] pixels = new byte[next * next];
                for (int y = 0; y < next; y++) {

                    for (int x = 0; x < next; x++) {

                        int at = y * 2 * width + x * 2;
                        int sum = 0;
                        for (int dz = 0; dz < 2; dz++) {

                            byte[] slice = level[z * 2 + dz];
                            sum += slice[at] + slice[at + 1] + slice[at + width] + slice[at + width + 1];

                        }
                        pixels[y * next + x] = (byte)((sum + 4) / 8);

                    }

                }
                reduced[z] = pixels;
                images.Add(Image.CreateFromData(next, next, false, Image.Format.L8, pixels));

            }
            level = reduced;
            width = next;

        }

        ImageTexture3D filtered = new();
        Error error = filtered.Create(Image.Format.L8, size, size, size, true, images);
        if (error != Error.Ok) {

            throw new InvalidOperationException($"Cloud mip volume creation failed: {error}");

        }
        return filtered;

    }

}
