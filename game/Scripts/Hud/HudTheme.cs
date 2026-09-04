using Godot;

namespace FullThrust.Game;

/// <summary>Every colour, face and box the flight interface draws with. Nothing else authors its own look.</summary>
public static class HudTheme {

    public static readonly Color Ink = new Color(0.906f, 0.933f, 0.961f);
    public static readonly Color Dim = new Color(0.573f, 0.647f, 0.714f);
    public static readonly Color Faint = new Color(0.353f, 0.412f, 0.475f);

    public static readonly Color Backing = new Color(0.016f, 0.022f, 0.031f, 0.92f);
    public static readonly Color Edge = new Color(0.706f, 0.784f, 0.863f, 0.26f);
    public static readonly Color Well = new Color(0.706f, 0.784f, 0.863f, 0.10f);

    // The whole colour vocabulary. State is carried by brightness; these three say what the grey
    // cannot, and nothing else in the interface is allowed a hue.
    public static readonly Color Fuel = new Color(0.918f, 0.773f, 0.588f);
    public static readonly Color Oxidiser = new Color(0.647f, 0.812f, 0.894f);
    public static readonly Color Caution = new Color(0.918f, 0.671f, 0.663f);

    // JetBrains Mono's cap height in ems. Digits carry no descender, so a numeric readout centred
    // on the line box sits low by half a descent; this is what it centres on instead.
    public const float NumeralCap = 0.73f;

    public const int Tiny = 9;
    public const int Small = 11;
    public const int Body = 13;
    public const int Head = 16;
    public const int Large = 20;

    public static Font Label { get; }
    public static Font Strong { get; }

    public static Font Numeral { get; }
    public static Font NumeralStrong { get; }

    // Controls carry their own ground rather than a wash over whatever is behind them; a button
    // that lets the planet through is a button whose label goes out over the day side.
    private static readonly StyleBoxFlat ChipIdle = Chip(new Color(0.043f, 0.055f, 0.071f, 0.94f), Edge);
    private static readonly StyleBoxFlat ChipHover = Chip(new Color(0.086f, 0.106f, 0.133f, 0.96f), new Color(0.706f, 0.784f, 0.863f, 0.42f));

    private static readonly StyleBoxFlat ChipLit = Chip(new Color(0.118f, 0.145f, 0.180f, 0.96f), new Color(0.906f, 0.933f, 0.961f, 0.66f));
    private static readonly StyleBoxFlat ChipLitHover = Chip(new Color(0.157f, 0.192f, 0.235f, 0.98f), new Color(0.906f, 0.933f, 0.961f, 0.90f));

    static HudTheme() {

        Label = GD.Load<Font>("res://Assets/Fonts/IBMPlexSansCondensed-Regular.ttf");
        Strong = GD.Load<Font>("res://Assets/Fonts/IBMPlexSansCondensed-SemiBold.ttf");

        Numeral = GD.Load<Font>("res://Assets/Fonts/JetBrainsMono-Regular.ttf");
        NumeralStrong = GD.Load<Font>("res://Assets/Fonts/JetBrainsMono-Medium.ttf");

    }

    /// <summary>The one box every panel in the interface is drawn on.</summary>
    public static StyleBoxFlat Panel(float margin = 9.0f) {

        StyleBoxFlat box = new StyleBoxFlat {

            BgColor = Backing,
            BorderColor = Edge,

            ContentMarginLeft = margin,
            ContentMarginRight = margin,
            ContentMarginTop = margin,
            ContentMarginBottom = margin,

        };

        box.SetBorderWidthAll(1);

        return box;

    }

    /// <summary>The recessed track a bar or a tape sits in.</summary>
    public static StyleBoxFlat Track() {

        return new StyleBoxFlat { BgColor = Well };

    }

    /// <summary>A pressable control. Focus is refused so the keyboard flight bindings never go deaf.</summary>
    public static Button Button(string text, Vector2 size) {

        Button button = new Button {

            Text = text,
            CustomMinimumSize = size,

            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,

        };

        button.AddThemeFontOverride("font", Strong);
        button.AddThemeFontSizeOverride("font_size", Small);

        Light(button, false);

        return button;

    }

    /// <summary>Repaints a control for its on or off state.</summary>
    public static void Light(Button button, bool lit) {

        button.AddThemeStyleboxOverride("normal", lit ? ChipLit : ChipIdle);
        button.AddThemeStyleboxOverride("hover", lit ? ChipLitHover : ChipHover);
        button.AddThemeStyleboxOverride("pressed", lit ? ChipLitHover : ChipHover);
        button.AddThemeStyleboxOverride("focus", lit ? ChipLit : ChipIdle);

        // Without this a shut control falls back to Godot's own box and disappears off the panel.
        // Shut is the darkest rung of the same ladder, not a different look.
        button.AddThemeStyleboxOverride("disabled", ChipIdle);

        button.AddThemeColorOverride("font_disabled_color", Faint);

        button.AddThemeColorOverride("font_color", lit ? Ink : Dim);
        button.AddThemeColorOverride("font_hover_color", Ink);
        button.AddThemeColorOverride("font_pressed_color", Ink);

    }

    /// <summary>Draws text from a baseline origin.</summary>
    public static void Write(CanvasItem canvas, Font font, int size, Vector2 baseline, string text, Color colour) {

        canvas.DrawString(font, baseline, text, HorizontalAlignment.Left, -1.0f, size, colour);

    }

    /// <summary>Draws text aligned inside a box and centred on its vertical middle.</summary>
    public static void WriteIn(CanvasItem canvas, Font font, int size, Rect2 box, string text, Color colour, HorizontalAlignment align) {

        float ascent = font.GetAscent(size);
        float descent = font.GetDescent(size);

        Vector2 baseline = new Vector2(box.Position.X, box.Position.Y + (box.Size.Y + ascent - descent) * 0.5f);

        canvas.DrawString(font, baseline, text, align, box.Size.X, size, colour);

    }

    public static float Width(Font font, int size, string text) => font.GetStringSize(text, HorizontalAlignment.Left, -1.0f, size).X;

    private static StyleBoxFlat Chip(Color fill, Color edge) {

        StyleBoxFlat box = new StyleBoxFlat {

            BgColor = fill,
            BorderColor = edge,

            ContentMarginLeft = 9.0f,
            ContentMarginRight = 9.0f,
            ContentMarginTop = 4.0f,
            ContentMarginBottom = 4.0f,

        };

        box.SetBorderWidthAll(1);

        return box;

    }

}
