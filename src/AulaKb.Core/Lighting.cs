namespace AulaKb.Core;

public sealed record Effect(byte Id, string Name, bool HiddenByVendor = false);

/// <summary>Hardware effects (profile byte 10). Names from the vendor tool's text.xml.</summary>
public static class Effects
{
    public const byte Off = 0x00;
    public const byte SelfDefine = 0x15;

    public static readonly IReadOnlyList<Effect> All =
    [
        new(0x01, "Fixed on"),
        new(0x02, "Respire"),
        new(0x03, "Rainbow"),
        new(0x04, "Flash away"),
        new(0x05, "Raindrops"),
        new(0x06, "Rainbow wheel", HiddenByVendor: true),
        new(0x07, "Ripples shining"),
        new(0x08, "Stars twinkle"),
        new(0x09, "Shadow disappear", HiddenByVendor: true),
        new(0x0A, "Retro snake"),
        new(0x0B, "Neon stream"),
        new(0x0C, "Reaction"),
        new(0x0D, "Sine wave"),
        new(0x0E, "Retinue scanning", HiddenByVendor: true),
        new(0x0F, "Rotating windmill"),
        new(0x10, "Colorful waterfall"),
        new(0x11, "Blossoming"),
        new(0x12, "Rotating storm", HiddenByVendor: true),
        new(SelfDefine, "Self-define (per-key)"),
        new(Off, "Off"),
    ];

    public static Effect Get(byte id) => All.FirstOrDefault(e => e.Id == id) ?? new Effect(id, $"Effect 0x{id:X2}");
}

/// <summary>
/// 128-byte lighting/settings profile (0x04 / 0x84). Ends with 5A A5.
///   [3]  debounce stage 0..4 (vendor UI shows 1..5)
///   [9]  1 while the self-define effect is active
///   [10] active effect id
///   [22] tap sensitivity, 0 = tap off
///   [56 + 2*id]     brightness 0..4 for effect id
///   [56 + 2*id + 1] (speed 0..4) &lt;&lt; 4 | colour slot (0..6 = palette slot, 7 = colourful)
/// </summary>
public sealed class LightProfile
{
    public const int Length = 128;
    public const int ColourfulSlot = 7;
    const int ParamBase = 56;
    readonly byte[] _data;

    public LightProfile(byte[] data)
    {
        if (data.Length != Length)
            throw new ArgumentException($"Profile must be {Length} bytes.");
        _data = (byte[])data.Clone();
    }

    public bool IsValid => _data[126] == 0x5A && _data[127] == 0xA5;

    public byte Debounce { get => _data[3]; set => _data[3] = Math.Clamp(value, (byte)0, (byte)4); }

    public byte Effect
    {
        get => _data[10];
        set
        {
            _data[10] = value;
            _data[9] = value == Effects.SelfDefine ? (byte)1 : (byte)0;
        }
    }

    public byte TapSensitivity { get => _data[22]; set => _data[22] = Math.Min(value, (byte)0x7F); }

    public byte GetBrightness(byte effect) => _data[ParamBase + 2 * effect];

    public void SetBrightness(byte effect, byte value) => _data[ParamBase + 2 * effect] = Math.Clamp(value, (byte)0, (byte)4);

    public byte GetSpeed(byte effect) => (byte)(_data[ParamBase + 2 * effect + 1] >> 4);

    public void SetSpeed(byte effect, byte value)
    {
        int i = ParamBase + 2 * effect + 1;
        _data[i] = (byte)((Math.Clamp((int)value, 0, 4) << 4) | (_data[i] & 0x0F));
    }

    public byte GetColourSlot(byte effect) => (byte)(_data[ParamBase + 2 * effect + 1] & 0x0F);

    public void SetColourSlot(byte effect, byte slot)
    {
        int i = ParamBase + 2 * effect + 1;
        _data[i] = (byte)((_data[i] & 0xF0) | Math.Clamp((int)slot, 0, ColourfulSlot));
    }

    public byte[] ToArray() => (byte[])_data.Clone();
}

/// <summary>Effect colour palette (0x0A / 0x8A): 7 RGB colours per effect id at 21 * id.</summary>
public sealed class Palette
{
    public const int Length = 512;
    public const int SlotsPerEffect = 7;
    readonly byte[] _data;

    public Palette(byte[] data)
    {
        if (data.Length != Length)
            throw new ArgumentException($"Palette must be {Length} bytes.");
        _data = (byte[])data.Clone();
    }

    public Rgb Get(byte effect, int slot)
    {
        int i = Offset(effect, slot);
        return new Rgb(_data[i], _data[i + 1], _data[i + 2]);
    }

    public void Set(byte effect, int slot, Rgb c)
    {
        int i = Offset(effect, slot);
        _data[i] = c.R;
        _data[i + 1] = c.G;
        _data[i + 2] = c.B;
    }

    static int Offset(byte effect, int slot)
    {
        if (slot is < 0 or >= SlotsPerEffect)
            throw new ArgumentOutOfRangeException(nameof(slot));
        int i = 21 * effect + 3 * slot;
        if (i + 2 >= 506)
            throw new ArgumentOutOfRangeException(nameof(effect));
        return i;
    }

    public byte[] ToArray() => (byte[])_data.Clone();
}

/// <summary>Self-define per-key colours (0x06 / 0x86): planes R[0..125], G[126..251], B[252..377].</summary>
public sealed class PerKeyColors
{
    public const int Length = 384;
    readonly byte[] _data;

    public PerKeyColors(byte[] data)
    {
        if (data.Length != Length)
            throw new ArgumentException($"Per-key table must be {Length} bytes.");
        _data = (byte[])data.Clone();
    }

    public Rgb this[int led]
    {
        get => new(_data[led], _data[AulaKeyboard.LedCount + led], _data[2 * AulaKeyboard.LedCount + led]);
        set
        {
            _data[led] = value.R;
            _data[AulaKeyboard.LedCount + led] = value.G;
            _data[2 * AulaKeyboard.LedCount + led] = value.B;
        }
    }

    public byte[] ToArray() => (byte[])_data.Clone();
}

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}
