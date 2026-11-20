using System.Windows.Media;

namespace PiggyHelper.Services;

/// <summary>根据一个主色生成卡片用的渐变、浅色底和阴影色（参考黏土风：阴影用同色系而不是灰色）。</summary>
public static class Palette
{
    private static readonly Dictionary<string, (Brush Icon, Brush Tint, Color Shadow, Brush Solid)> Cache = new();

    public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    public static (Brush Icon, Brush Tint, Color Shadow, Brush Solid) For(string hex)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(hex, out var v)) return v;
            var c = Parse(hex);
            var icon = new LinearGradientBrush(Mix(c, Colors.White, 0.35), c, 45);
            icon.Freeze();
            var tint = new SolidColorBrush(Mix(c, Colors.White, 0.86));
            tint.Freeze();
            var solid = new SolidColorBrush(c);
            solid.Freeze();
            v = (icon, tint, Mix(c, Colors.Black, 0.15), solid);
            Cache[hex] = v;
            return v;
        }
    }
}
