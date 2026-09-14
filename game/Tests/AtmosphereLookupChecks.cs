using System;

using Godot;

namespace FullThrust.Game;

public sealed partial class AtmosphereLookupChecks : Node {

    public override void _Ready() {

        const double radius = 1_274_200.0;
        const double top = radius + 75_000.0;
        using ImageTexture texture = AtmosphereLookup.Build(radius, top, 5600, 1200, 22000, 15000);
        using Image image = texture.GetImage();
        int failed = 0;
        int count = 0;
        double maximum = 0;

        foreach (double height in new[] { 0.0, 20.0, 900.0, 3500.0, 15000.0, 45000.0, 74000.0 }) {

            foreach (double cosine in new[] { -0.05, 0.0, 0.015, 0.15, 0.5, 1.0 }) {

                double tangent = -Math.Sqrt(1.0 - radius * radius / Math.Pow(radius + height, 2.0));
                if (cosine < tangent) { continue; }
                var expected = AtmosphereLookup.Integrate(radius, top, height, cosine, 5600, 1200, 22000, 15000, 2048);
                float x = (float)(0.5 + 0.5 * Math.Sign(cosine) * Math.Sqrt(Math.Abs(cosine))) * (AtmosphereLookup.Width - 1);
                float y = (float)Math.Sqrt(height / (top - radius)) * (AtmosphereLookup.Height - 1);
                int left = Mathf.FloorToInt(x);
                int low = Mathf.FloorToInt(y);
                int right = Math.Min(left + 1, AtmosphereLookup.Width - 1);
                int high = Math.Min(low + 1, AtmosphereLookup.Height - 1);
                Color sampled = image.GetPixel(left, low).Lerp(image.GetPixel(right, low), x - left)
                    .Lerp(image.GetPixel(left, high).Lerp(image.GetPixel(right, high), x - left), y - low);
                double[] wanted = { expected.X, expected.Y, expected.Z };
                double[] actual = { sampled.R * 5600, sampled.G * 1200, sampled.B * 15000 };

                for (int channel = 0; channel < 3; channel++) {

                    double error = Math.Abs(wanted[channel] - actual[channel]) / Math.Max(wanted[channel], 10.0);
                    maximum = Math.Max(maximum, error);
                    count++;

                    if (!double.IsFinite(actual[channel]) || error > 0.06) {

                        failed++;
                        GD.PrintErr($"LUT error h={height} mu={cosine} channel={channel}: {error:P2}");

                    }

                }

            }

        }

        GD.Print($"ATMOSPHERE LOOKUP {count - failed}/{count}; maximum normalized error {maximum:P2}");
        GetTree().Quit(failed == 0 ? 0 : 1);

    }

}
