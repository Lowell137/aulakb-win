using AulaKb.Core;

namespace AulaKb.App.Controls;

/// <summary>
/// On-screen approximation of the keyboard's hardware effects, so the selected effect can be
/// seen moving on the drawn keyboard. The firmware renders the real thing; this only mimics it.
/// </summary>
sealed class EffectPreview
{
    readonly IReadOnlyList<KeyInfo> _keys;
    readonly (double X, double Y)[] _pos;
    readonly double[] _spark;
    readonly Random _random = new(7);
    readonly double _cx;
    readonly double _cy;

    public EffectPreview(IReadOnlyList<KeyInfo> keys)
    {
        _keys = keys;
        double minX = _keys.Min(k => k.X), maxX = _keys.Max(k => k.X + k.W);
        double minY = _keys.Min(k => k.Y), maxY = _keys.Max(k => k.Y + k.H);
        // Normalised key centres: x 0..1, y 0..1.
        _pos = _keys.Select(k => ((k.X + k.W / 2.0 - minX) / (maxX - minX), (k.Y + k.H / 2.0 - minY) / (maxY - minY))).ToArray();
        _spark = new double[_keys.Count];
        _cx = 0.5;
        _cy = 0.5;
    }

    public IReadOnlyList<KeyInfo> Keys => _keys;

    /// <summary>Colour of every key at time t (seconds). Index matches <see cref="Keys"/>.</summary>
    public Rgb[] Render(byte effect, double t, int brightness, int speed, bool colourful, Rgb colour, PerKeyColors? perKey)
    {
        var result = new Rgb[_keys.Count];
        double s = 0.35 + 0.35 * speed;              // speed 0..4
        double level = brightness / 4.0;             // brightness 0..4
        double tt = t * s;

        for (int i = 0; i < _keys.Count; i++)
        {
            var (x, y) = _pos[i];
            double dx = x - _cx, dy = (y - _cy) * 0.4;
            double r = Math.Sqrt(dx * dx + dy * dy);
            double angle = Math.Atan2(dy, dx) / (2 * Math.PI) + 0.5;
            double hue;
            double amount;

            switch (effect)
            {
                case Effects.Off:
                    result[i] = Rgb.Black;
                    continue;
                case Effects.SelfDefine:
                    result[i] = perKey is null ? Rgb.Black : Scale(perKey[_keys[i].Led], level == 0 ? 1 : level);
                    continue;
                case 0x01: // fixed on
                    hue = x;
                    amount = 1;
                    break;
                case 0x02: // respire
                    hue = tt * 0.1;
                    amount = 0.5 - 0.5 * Math.Cos(tt * 2);
                    break;
                case 0x03: // rainbow
                    hue = x - tt * 0.25;
                    amount = 1;
                    break;
                case 0x06: // rainbow wheel
                    hue = angle + tt * 0.25;
                    amount = 1;
                    break;
                case 0x0B: // neon stream
                    hue = x * 0.6 + tt * 0.2;
                    amount = 1;
                    break;
                case 0x0D: // sine wave
                    hue = x - tt * 0.15;
                    amount = Math.Clamp(1 - Math.Abs(y - (0.5 + 0.4 * Math.Sin(x * 9 - tt * 3))) * 3, 0, 1);
                    break;
                case 0x0E: // retinue scanning
                    hue = tt * 0.1;
                    amount = Math.Clamp(1 - Math.Abs(x - Tri(tt * 0.4)) * 7, 0, 1);
                    break;
                case 0x0F: // rotating windmill
                    hue = angle;
                    amount = Frac(angle * 4 - tt * 0.5) < 0.5 ? 1 : 0.08;
                    break;
                case 0x10: // colorful waterfall
                    hue = y - tt * 0.3 + x * 0.2;
                    amount = 1;
                    break;
                case 0x11: // blossoming
                    hue = r - tt * 0.2;
                    amount = Math.Clamp(1 - Math.Abs(Frac(r * 2.2 - tt * 0.5) - 0.5) * 3, 0, 1);
                    break;
                case 0x12: // rotating storm
                    hue = angle + r;
                    amount = Frac(angle * 3 + r * 4 - tt * 0.6) < 0.4 ? 1 : 0.1;
                    break;
                case 0x07: // ripples shining
                {
                    double best = 0;
                    for (int n = 0; n < 3; n++)
                    {
                        double phase = Frac(tt * 0.35 + n / 3.0);
                        var (ox, oy) = Origin(n, Math.Floor(tt * 0.35 + n / 3.0));
                        double d = Math.Sqrt((x - ox) * (x - ox) + (y - oy) * (y - oy) * 0.16);
                        best = Math.Max(best, Math.Clamp(1 - Math.Abs(d - phase * 0.9) * 10, 0, 1) * (1 - phase));
                    }
                    hue = r + tt * 0.1;
                    amount = best;
                    break;
                }
                case 0x0A: // retro snake
                {
                    int count = _keys.Count;
                    double head = Frac(tt * 0.12) * count;
                    double dist = (head - Order(i) + count) % count;
                    hue = dist / 20.0;
                    amount = dist < 14 ? 1 - dist / 14 : 0;
                    break;
                }
                default: // flash away, raindrops, stars twinkle, shadow disappear, reaction: random sparkles
                {
                    double rate = effect switch { 0x08 => 0.012, 0x05 => 0.008, 0x0C => 0.006, _ => 0.01 };
                    double decay = effect == 0x08 ? 0.94 : 0.9;
                    _spark[i] *= decay;
                    if (_random.NextDouble() < rate * (0.6 + speed * 0.3))
                        _spark[i] = 1;
                    hue = i * 0.137;
                    amount = effect == 0x09 ? 1 - _spark[i] : _spark[i];
                    break;
                }
            }

            var c = colourful ? Hue(hue) : colour;
            result[i] = Scale(c, amount * Math.Max(level, 0.15));
        }
        return result;
    }

    int Order(int i) => _keys[i].Led;

    (double, double) Origin(int n, double cycle)
    {
        var rnd = new Random((int)cycle * 31 + n * 7);
        return (rnd.NextDouble(), rnd.NextDouble());
    }

    static double Frac(double v) => v - Math.Floor(v);

    static double Tri(double v) => 1 - Math.Abs(Frac(v) * 2 - 1);

    static Rgb Scale(Rgb c, double k) => new((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));

    public static Rgb Hue(double h)
    {
        h = Frac(h);
        double r = Math.Clamp(Math.Abs(h * 6 - 3) - 1, 0, 1);
        double g = Math.Clamp(2 - Math.Abs(h * 6 - 2), 0, 1);
        double b = Math.Clamp(2 - Math.Abs(h * 6 - 4), 0, 1);
        return new Rgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}
