using System.Reflection;
using System.Text.Json;

namespace AulaKb.Core;

/// <summary>Factory tables captured from the vendor tool's Reset, and the key layout from its KB.ini.</summary>
public static class Defaults
{
    public static LightProfile Profile() => new(Load("profile.bin"));

    public static Palette Palette() => new(Load("palette.bin"));

    public static PerKeyColors PerKeyColors() => new(Load("perkey.bin"));

    /// <summary>Matrix index of the ISO key left of Enter (Non-US #; "," on Turkish Q).</summary>
    public const int IsoHashIndex = 75;

    /// <summary>Matrix index of the ISO key right of left Shift (Non-US \; "&lt;" on Turkish Q).</summary>
    public const int IsoBackslashIndex = 76;

    public static KeyLayer Layer(Layer layer)
    {
        var keys = new KeyLayer(Load(layer switch
        {
            Core.Layer.Fn1 => "matrix_fn1.bin",
            Core.Layer.Fn2 => "matrix_fn2.bin",
            Core.Layer.Tap => "matrix_tap.bin",
            _ => "matrix_default.bin",
        }));
        if (layer == Core.Layer.Default)
        {
            // The vendor "wired" tables are ANSI and leave the two ISO keys empty, so they type
            // nothing. Found on an ISO Turkish F87 Pro by giving every empty index a probe key.
            keys[IsoHashIndex] = KeyAction.Key(0x32);
            keys[IsoBackslashIndex] = KeyAction.Key(0x64);
        }
        return keys;
    }

    static byte[] Load(string name)
    {
        using var s = Open(name);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    internal static Stream Open(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream($"AulaKb.Core.Defaults.{name}")
        ?? throw new FileNotFoundException(name);
}

/// <summary>A physical key: label, rectangle in layout units, LED/matrix index, default HID usage.</summary>
public sealed record KeyInfo(int Id, string Label, int X, int Y, int W, int H, int Vk, int Led);

public static class KeyLayout
{
    static IReadOnlyList<KeyInfo>? _keys;

    public static IReadOnlyList<KeyInfo> Keys => _keys ??= Load();

    static IReadOnlyList<KeyInfo> Load()
    {
        using var s = Defaults.Open("layout.json");
        var raw = JsonSerializer.Deserialize<List<RawKey>>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        return raw.Select(k => new KeyInfo(k.Id, k.Label, k.X, k.Y, k.W, k.H, k.Vk, k.Led)).ToList();
    }

    sealed record RawKey(int Id, string Label, int X, int Y, int W, int H, int Vk, int Led);
}
