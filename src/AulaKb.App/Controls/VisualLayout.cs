using System.Text.Json;
using AulaKb.Core;

namespace AulaKb.App.Controls;

public enum LayoutKind
{
    TurkishQIso,
    EnglishIso,
    EnglishAnsi,
}

public enum CapKind
{
    Light,
    Dark,
    Accent,
}

public readonly record struct KeyRect(double X, double Y, double W, double H);

/// <summary>A key as drawn: its LED/matrix identity, one or more rectangles (ISO Enter is two), label and cap colour.</summary>
public sealed record VisualKey(KeyInfo Key, IReadOnlyList<KeyRect> Rects, string Label, CapKind Cap);

/// <summary>
/// Physical layouts of the F87 Pro drawn from the vendor KB.ini geometry (ANSI). ISO turns the
/// backslash key into the top of an L-shaped Enter, adds the key left of Enter (matrix/LED 75)
/// and splits left Shift for the key next to it (76). Labels and cap colours follow the
/// Turkish Q keyboard this was built on.
/// </summary>
public static class VisualLayout
{
    // LED indices of the keys we restyle (column * 6 + row).
    const int Esc = 0, Grave = 1, LShift = 4, Backslash = 80, Enter = 81, Space = 35;

    public static readonly IReadOnlyList<(LayoutKind Kind, string Name)> All =
    [
        (LayoutKind.TurkishQIso, "Türkçe Q (ISO)"),
        (LayoutKind.EnglishIso, "İngilizce (ISO, UK)"),
        (LayoutKind.EnglishAnsi, "İngilizce (ANSI, ABD)"),
    ];

    static readonly HashSet<int> AccentKeys = [Esc, Enter, Space, 89, 94, 95, 101];

    static readonly HashSet<int> DarkKeys =
    [
        36, 42, 48, 54,                 // F5-F8
        84, 90, 96, 85, 91, 97, 86, 92, 98, // print/scroll/pause, nav cluster
        79, 2, 3, LShift, 82,           // backspace, tab, caps, shifts
        5, 11, 17, 53, 59, 65, 83,      // bottom row modifiers
    ];

    static readonly Dictionary<int, string> Common = new()
    {
        [Esc] = "Esc", [79] = "← Backspace", [2] = "Tab", [3] = "Caps Lock", [LShift] = "Shift", [82] = "Shift",
        [Enter] = "Enter ↵", [5] = "Ctrl", [11] = "Win", [17] = "Alt", [Space] = "", [53] = "Alt Gr", [59] = "Fn",
        [65] = "≡", [83] = "Ctrl", [84] = "Print", [90] = "Scroll", [96] = "Pause", [85] = "Ins", [91] = "Home",
        [97] = "PgUp", [86] = "Del", [92] = "End", [98] = "PgDn", [94] = "↑", [89] = "←", [95] = "↓", [101] = "→",
    };

    static readonly Dictionary<int, string> Turkish = new()
    {
        [Grave] = "\" é", [7] = "!\n1", [13] = "'\n2", [19] = "^\n3", [25] = "+\n4", [31] = "%\n5", [37] = "&\n6",
        [43] = "/\n7", [49] = "(\n8", [55] = ")\n9", [61] = "=\n0", [67] = "?\n*", [73] = "_\n-",
        [68] = "Ğ", [74] = "Ü", [63] = "Ş", [69] = "İ", [75] = ";\n,", [76] = ">\n<", [52] = "Ö", [58] = "Ç", [64] = ":\n.",
    };

    static readonly Dictionary<int, string> EnglishIso = new()
    {
        [Grave] = "¬\n`", [7] = "!\n1", [13] = "\"\n2", [19] = "£\n3", [25] = "$\n4", [31] = "%\n5", [37] = "^\n6",
        [43] = "&\n7", [49] = "*\n8", [55] = "(\n9", [61] = ")\n0", [67] = "_\n-", [73] = "+\n=",
        [68] = "{\n[", [74] = "}\n]", [63] = ":\n;", [69] = "@\n'", [75] = "~\n#", [76] = "|\n\\", [52] = "<\n,", [58] = ">\n.", [64] = "?\n/",
    };

    static readonly Dictionary<int, string> EnglishAnsi = new()
    {
        [Grave] = "~\n`", [7] = "!\n1", [13] = "@\n2", [19] = "#\n3", [25] = "$\n4", [31] = "%\n5", [37] = "^\n6",
        [43] = "&\n7", [49] = "*\n8", [55] = "(\n9", [61] = ")\n0", [67] = "_\n-", [73] = "+\n=",
        [68] = "{\n[", [74] = "}\n]", [Backslash] = "|\n\\", [63] = ":\n;", [69] = "\"\n'", [52] = "<\n,", [58] = ">\n.", [64] = "?\n/",
    };

    public static LayoutKind Current
    {
        get => AppSettings.Load().Layout;
        set
        {
            var s = AppSettings.Load();
            s.Layout = value;
            s.Save();
        }
    }

    public static IReadOnlyList<VisualKey> Build(LayoutKind kind)
    {
        var byLed = KeyLayout.Keys.ToDictionary(k => k.Led);
        var labels = kind switch
        {
            LayoutKind.TurkishQIso => Turkish,
            LayoutKind.EnglishIso => EnglishIso,
            _ => EnglishAnsi,
        };
        bool iso = kind != LayoutKind.EnglishAnsi;

        KeyInfo q = byLed[8], w = byLed[14];
        double unit = w.X - q.X;   // key pitch
        double cap = q.W;          // 1u keycap width
        double gap = unit - cap;

        var result = new List<VisualKey>();
        foreach (var key in KeyLayout.Keys)
        {
            if (iso && key.Led is Backslash or Enter or LShift)
                continue;
            result.Add(Make(key, [new KeyRect(key.X, key.Y, key.W, key.H)], labels));
        }

        if (iso)
        {
            KeyInfo shift = byLed[LShift], enter = byLed[Enter], backslash = byLed[Backslash];
            double shiftW = Math.Round(unit * 1.25 - gap);
            result.Add(Make(shift, [new KeyRect(shift.X, shift.Y, shiftW, shift.H)], labels));
            var lessThan = new KeyInfo(1000 + Defaults.IsoBackslashIndex, "ISO <", (int)(shift.X + shiftW + gap), shift.Y, (int)cap, shift.H, 0xE2, Defaults.IsoBackslashIndex);
            result.Add(Make(lessThan, [new KeyRect(lessThan.X, lessThan.Y, lessThan.W, lessThan.H)], labels));

            var hash = new KeyInfo(1000 + Defaults.IsoHashIndex, "ISO #", enter.X, enter.Y, (int)cap, enter.H, 0, Defaults.IsoHashIndex);
            result.Add(Make(hash, [new KeyRect(hash.X, hash.Y, hash.W, hash.H)], labels));

            double right = enter.X + enter.W;
            double lowX = enter.X + unit;
            var isoEnter = enter with { X = backslash.X, Y = backslash.Y, W = (int)(right - backslash.X), H = enter.Y + enter.H - backslash.Y };
            // Wide top part plus a narrow part reaching down through the home row (inverted L).
            result.Add(Make(isoEnter,
            [
                new KeyRect(backslash.X, backslash.Y, right - backslash.X, backslash.H),
                new KeyRect(lowX, backslash.Y, right - lowX, enter.Y + enter.H - backslash.Y),
            ], labels));
        }
        return result;
    }

    static VisualKey Make(KeyInfo key, IReadOnlyList<KeyRect> rects, Dictionary<int, string> labels)
    {
        string label = labels.TryGetValue(key.Led, out var l) ? l
            : Common.TryGetValue(key.Led, out var c) ? c
            : key.Label;
        var kind = AccentKeys.Contains(key.Led) ? CapKind.Accent : DarkKeys.Contains(key.Led) ? CapKind.Dark : CapKind.Light;
        return new VisualKey(key with { Label = label.Replace('\n', ' ') }, rects, label, kind);
    }
}

/// <summary>Small per-user settings file (%LOCALAPPDATA%\AulaKb\settings.json); the app is unpackaged.</summary>
public sealed class AppSettings
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AulaKb", "settings.json");

    public LayoutKind Layout { get; set; } = LayoutKind.TurkishQIso;

    /// <summary>UI language override: null follows Windows, otherwise tr-TR / en-US.</summary>
    public string? Language { get; set; }

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch (Exception)
        {
            return new();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}
