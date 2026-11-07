namespace AulaKb.Core;

/// <summary>Key matrix layers (0x03 / 0x83 sub command).</summary>
public enum Layer : byte
{
    Default = 0,
    Fn1 = 1,
    Fn2 = 2,
    Tap = 3,
}

public enum KeyActionType : byte
{
    Keyboard = 0x00,
    Mouse = 0x01,
    Consumer = 0x02,
    Macro = 0x03,
    Function = 0x07,
    Lighting = 0x08,
}

[Flags]
public enum Modifiers : byte
{
    None = 0,
    LCtrl = 0x01,
    LShift = 0x02,
    LAlt = 0x04,
    LWin = 0x08,
    RCtrl = 0x10,
    RShift = 0x20,
    RAlt = 0x40,
    RWin = 0x80,
}

public enum MacroRepeat : byte
{
    Times = 1,
    UntilAnyKey = 2,
    WhileHeld = 4,
}

/// <summary>
/// One 4-byte matrix entry.
///   Keyboard  00 mods key1 key2   (plain key in key2; disabled = 00 00 00 00)
///   Mouse     01 button 01 00     (button 1..5)
///   Consumer  02 00 usage_hi usage_lo
///   Macro     03 repeat count index
///   Function  07 00 00 code       (1 Win lock, 2 keyboard lock, 4 reset, others on the Fn layer)
///   Lighting  08 param a b        (Fn-layer light controls)
/// </summary>
public readonly record struct KeyAction(byte Type, byte A, byte B, byte C)
{
    public static readonly KeyAction Disabled = new(0, 0, 0, 0);

    public KeyActionType Kind => (KeyActionType)Type;

    public static KeyAction Key(byte usage, Modifiers mods = Modifiers.None) =>
        usage == 0 && mods != Modifiers.None ? new(0, (byte)mods, 0, 0) : new(0, (byte)mods, 0, usage);

    public static KeyAction MouseButton(byte button) => new(1, button, 1, 0);

    public static KeyAction Consumer(ushort usage) => new(2, 0, (byte)(usage >> 8), (byte)usage);

    public static KeyAction Macro(byte index, MacroRepeat repeat, byte count = 1) => new(3, (byte)repeat, count, index);

    public static KeyAction Function(byte code) => new(7, 0, 0, code);

    public ushort ConsumerUsage => (ushort)((B << 8) | C);

    public Modifiers Mods => (Modifiers)A;

    public string Describe()
    {
        switch (Kind)
        {
            case KeyActionType.Keyboard:
                if (this == Disabled)
                    return "Devre dışı";
                var parts = new List<string>();
                foreach (Modifiers m in Enum.GetValues<Modifiers>())
                    if (m != Modifiers.None && Mods.HasFlag(m))
                        parts.Add(m.ToString());
                if (B != 0)
                    parts.Add(HidUsage.Name(B));
                if (C != 0)
                    parts.Add(HidUsage.Name(C));
                return string.Join(" + ", parts);
            case KeyActionType.Mouse:
                return A switch { 1 => "Fare sol", 2 => "Fare sağ", 3 => "Fare orta", 4 => "Fare geri", 5 => "Fare ileri", _ => $"Fare {A}" };
            case KeyActionType.Consumer:
                return ConsumerUsages.Name(ConsumerUsage);
            case KeyActionType.Macro:
                string how = (MacroRepeat)A switch
                {
                    MacroRepeat.WhileHeld => "basılı tutulduğu sürece",
                    MacroRepeat.UntilAnyKey => "bir tuşa basılana kadar",
                    _ => $"{B} kez",
                };
                return $"Makro {C + 1} ({how})";
            case KeyActionType.Function:
                return FunctionCodes.Name(C);
            case KeyActionType.Lighting:
                return LightingControls.Name(A, B, C);
            default:
                return $"{Type:X2} {A:X2} {B:X2} {C:X2}";
        }
    }
}

/// <summary>Key matrix layer (512 bytes = 128 entries x 4, ends with 5A A5), indexed by LED/key index.</summary>
public sealed class KeyLayer
{
    public const int Length = 512;
    public const int Entries = 127; // entry 127 holds the 5A A5 trailer
    readonly byte[] _data;

    public KeyLayer(byte[] data)
    {
        if (data.Length != Length)
            throw new ArgumentException($"Layer must be {Length} bytes.");
        _data = (byte[])data.Clone();
    }

    public bool IsValid => _data[510] == 0x5A && _data[511] == 0xA5;

    public KeyAction this[int index]
    {
        get
        {
            Check(index);
            int i = index * 4;
            return new KeyAction(_data[i], _data[i + 1], _data[i + 2], _data[i + 3]);
        }
        set
        {
            Check(index);
            int i = index * 4;
            _data[i] = value.Type;
            _data[i + 1] = value.A;
            _data[i + 2] = value.B;
            _data[i + 3] = value.C;
        }
    }

    static void Check(int index)
    {
        if (index is < 0 or >= Entries)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    public byte[] ToArray() => (byte[])_data.Clone();
}

public static class FunctionCodes
{
    public const byte WinLock = 0x01;
    public const byte KeyboardLock = 0x02;
    public const byte FactoryReset = 0x04;

    public static string Name(byte code) => code switch
    {
        WinLock => "Win kilidi",
        KeyboardLock => "Klavye kilidi",
        FactoryReset => "Fabrika ayarı (Fn+Esc)",
        _ => $"Klavye fonksiyonu 0x{code:X2}",
    };
}

public static class LightingControls
{
    public static string Name(byte param, byte a, byte b)
    {
        string what = param switch { 0 => "Efekt", 2 => "Renk", 3 => "Parlaklık", 4 => "Hız", _ => $"Işık {param:X2}" };
        string dir = (a, b) switch
        {
            (1, 0) => " +",
            (2, 0) => " −",
            (0, 1) => " (ileri)",
            (0, 2) => " (geri)",
            (0, 0) => " değiştir",
            _ => $" {a:X2} {b:X2}",
        };
        return what + dir;
    }
}

/// <summary>Consumer usages offered by the vendor tool (Multimedia tab).</summary>
public static class ConsumerUsages
{
    public static readonly IReadOnlyList<(ushort Usage, string Name)> All =
    [
        (0x00CD, "Oynat / Duraklat"),
        (0x00E2, "Sessiz"),
        (0x00B7, "Durdur"),
        (0x00B6, "Önceki parça"),
        (0x00B5, "Sonraki parça"),
        (0x00E9, "Ses +"),
        (0x00EA, "Ses −"),
        (0x0223, "Tarayıcı ana sayfa"),
        (0x0192, "Hesap makinesi"),
        (0x018A, "Posta"),
        (0x0194, "Bilgisayarım"),
        (0x022A, "Sık kullanılanlar"),
        (0x006F, "Ekran parlaklığı +"),
        (0x0070, "Ekran parlaklığı −"),
    ];

    public static string Name(ushort usage) =>
        All.FirstOrDefault(u => u.Usage == usage).Name ?? $"Medya 0x{usage:X4}";
}

/// <summary>Shortcut "commands" from the vendor tool, stored as plain key combinations.</summary>
public static class Commands
{
    public static readonly IReadOnlyList<(string Name, KeyAction Action)> All =
    [
        ("Pencereyi kapat (Alt+F4)", new KeyAction(0, (byte)Modifiers.LAlt, 0x3D, 0)),
        ("Bilgisayarı kilitle (Win+L)", new KeyAction(0, (byte)Modifiers.LWin, 0x0F, 0)),
        ("Çalıştır (Win+R)", new KeyAction(0, (byte)Modifiers.LWin, 0x15, 0)),
        ("Masaüstünü göster (Win+D)", new KeyAction(0, (byte)Modifiers.LWin, 0x07, 0)),
        ("Yakınlaştır (Ctrl+=)", new KeyAction(0, (byte)Modifiers.LCtrl, 0x2E, 0)),
        ("Uzaklaştır (Ctrl+−)", new KeyAction(0, (byte)Modifiers.LCtrl, 0x2D, 0)),
        ("Görev yöneticisi (Ctrl+Alt+Del)", new KeyAction(0, (byte)(Modifiers.LCtrl | Modifiers.LAlt), 0x4C, 0)),
        ("Geri al (Ctrl+Z)", new KeyAction(0, (byte)Modifiers.LCtrl, 0x1D, 0)),
        ("Kaydet (Ctrl+S)", new KeyAction(0, (byte)Modifiers.LCtrl, 0x16, 0)),
        ("Tuşu devre dışı bırak", KeyAction.Disabled),
        ("Win kilidi", KeyAction.Function(FunctionCodes.WinLock)),
        ("Klavye kilidi", KeyAction.Function(FunctionCodes.KeyboardLock)),
        ("Fabrika ayarı", KeyAction.Function(FunctionCodes.FactoryReset)),
    ];
}

/// <summary>HID keyboard usage names (page 0x07).</summary>
public static class HidUsage
{
    static readonly Dictionary<byte, string> Names = Build();

    public static IReadOnlyDictionary<byte, string> All => Names;

    public static string Name(byte usage) => Names.TryGetValue(usage, out var n) ? n : $"0x{usage:X2}";

    static Dictionary<byte, string> Build()
    {
        var d = new Dictionary<byte, string>();
        for (int i = 0; i < 26; i++)
            d[(byte)(0x04 + i)] = ((char)('A' + i)).ToString();
        for (int i = 0; i < 9; i++)
            d[(byte)(0x1E + i)] = ((char)('1' + i)).ToString();
        d[0x27] = "0";
        string[] misc = ["Enter", "Esc", "Backspace", "Tab", "Space", "-", "=", "[", "]", "\\", "#", ";", "'", "`", ",", ".", "/", "Caps Lock"];
        for (int i = 0; i < misc.Length; i++)
            d[(byte)(0x28 + i)] = misc[i];
        for (int i = 0; i < 12; i++)
            d[(byte)(0x3A + i)] = $"F{i + 1}";
        string[] nav = ["Print Screen", "Scroll Lock", "Pause", "Insert", "Home", "Page Up", "Delete", "End", "Page Down", "→", "←", "↓", "↑", "Num Lock"];
        for (int i = 0; i < nav.Length; i++)
            d[(byte)(0x46 + i)] = nav[i];
        string[] pad = ["Num /", "Num *", "Num -", "Num +", "Num Enter", "Num 1", "Num 2", "Num 3", "Num 4", "Num 5", "Num 6", "Num 7", "Num 8", "Num 9", "Num 0", "Num ."];
        for (int i = 0; i < pad.Length; i++)
            d[(byte)(0x54 + i)] = pad[i];
        d[0x64] = "ISO \\ (<>)";
        d[0x65] = "Menü";
        for (int i = 0; i < 12; i++)
            d[(byte)(0x68 + i)] = $"F{i + 13}";
        string[] mods = ["LCtrl", "LShift", "LAlt", "LWin", "RCtrl", "RShift", "RAlt", "RWin"];
        for (int i = 0; i < mods.Length; i++)
            d[(byte)(0xE0 + i)] = mods[i];
        return d;
    }
}
