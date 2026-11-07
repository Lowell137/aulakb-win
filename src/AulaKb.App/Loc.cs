using AulaKb.Core;
using Microsoft.Windows.ApplicationModel.Resources;

namespace AulaKb.App;

/// <summary>
/// UI strings. They live in Strings\en-US\Resources.resw (English, the fallback) and
/// Strings\tr-TR\Resources.resw; Windows' preferred language picks the best match, and the
/// Settings page can override it (AppSettings.Language → ApplicationLanguages.PrimaryLanguageOverride,
/// applied before the first UI is created). Physical layout names in VisualLayout and key cap
/// labels are not part of this: they describe the hardware, not the UI language.
/// Strings the Core library produces in Turkish are re-expressed here in English so the app can
/// switch fully; the CLI (Turkish paths in Core) is unchanged.
/// </summary>
public static class Loc
{
    public const string Turkish = "tr-TR";
    public const string English = "en-US";

    static readonly ResourceLoader Loader = new();

    /// <summary>Which language the resource system actually picked, via the resw sentinel.</summary>
    public static bool IsEnglish => S("app.lang") == "en";

    // The packaged priconfig runs resw indexing with convertDotsToSlashes, so "nav.keys" became
    // the tree key nav/keys: GetString wants slashes, resw keeps dots.
    public static string S(string key) => Loader.GetString(key.Replace('.', '/')) is { Length: > 0 } s ? s : key;

    public static string F(string key, params object?[] args) => string.Format(S(key), args);

    /// <summary>HID keyboard usage name; the only Turkish entry in Core's table is Menü.</summary>
    public static string KeyName(byte usage) => usage == 0x65 && IsEnglish ? "Menu" : HidUsage.Name(usage);

    static readonly IReadOnlyList<(ushort Usage, string Name)> ConsumerUsagesEn =
    [
        (0x00CD, "Play / Pause"),
        (0x00E2, "Mute"),
        (0x00B7, "Stop"),
        (0x00B6, "Previous track"),
        (0x00B5, "Next track"),
        (0x00E9, "Volume +"),
        (0x00EA, "Volume −"),
        (0x0223, "Browser home"),
        (0x0192, "Calculator"),
        (0x018A, "Mail"),
        (0x0194, "My computer"),
        (0x022A, "Favorites"),
        (0x006F, "Brightness +"),
        (0x0070, "Brightness −"),
    ];

    public static IReadOnlyList<(ushort Usage, string Name)> ConsumerUsages() =>
        IsEnglish ? ConsumerUsagesEn : Core.ConsumerUsages.All;

    public static string ConsumerName(ushort usage) =>
        ConsumerUsages().FirstOrDefault(u => u.Usage == usage).Name ?? $"Media 0x{usage:X4}";

    static readonly IReadOnlyList<(string Name, KeyAction Action)> CommandsEn =
    [
        ("Close window (Alt+F4)", new KeyAction(0, (byte)Modifiers.LAlt, 0x3D, 0)),
        ("Lock the PC (Win+L)", new KeyAction(0, (byte)Modifiers.LWin, 0x0F, 0)),
        ("Run (Win+R)", new KeyAction(0, (byte)Modifiers.LWin, 0x15, 0)),
        ("Show desktop (Win+D)", new KeyAction(0, (byte)Modifiers.LWin, 0x07, 0)),
        ("Zoom in (Ctrl+=)", new KeyAction(0, (byte)Modifiers.LCtrl, 0x2E, 0)),
        ("Zoom out (Ctrl+−)", new KeyAction(0, (byte)Modifiers.LCtrl, 0x2D, 0)),
        ("Task manager (Ctrl+Alt+Del)", new KeyAction(0, (byte)(Modifiers.LCtrl | Modifiers.LAlt), 0x4C, 0)),
        ("Undo (Ctrl+Z)", new KeyAction(0, (byte)Modifiers.LCtrl, 0x1D, 0)),
        ("Save (Ctrl+S)", new KeyAction(0, (byte)Modifiers.LCtrl, 0x16, 0)),
        ("Disable key", KeyAction.Disabled),
        ("Win lock", KeyAction.Function(FunctionCodes.WinLock)),
        ("Keyboard lock", KeyAction.Function(FunctionCodes.KeyboardLock)),
        ("Factory reset", KeyAction.Function(FunctionCodes.FactoryReset)),
    ];

    public static IReadOnlyList<(string Name, KeyAction Action)> Commands() =>
        IsEnglish ? CommandsEn : Core.Commands.All;

    static string FunctionName(byte code) => IsEnglish
        ? code switch
        {
            FunctionCodes.WinLock => "Win lock",
            FunctionCodes.KeyboardLock => "Keyboard lock",
            FunctionCodes.FactoryReset => "Factory reset (Fn+Esc)",
            _ => $"Keyboard function 0x{code:X2}",
        }
        : FunctionCodes.Name(code);

    static string LightingName(byte param, byte a, byte b)
    {
        if (!IsEnglish)
            return LightingControls.Name(param, a, b);
        string what = param switch { 0 => "Effect", 2 => "Colour", 3 => "Brightness", 4 => "Speed", _ => $"Lighting {param:X2}" };
        string dir = (a, b) switch
        {
            (1, 0) => " +",
            (2, 0) => " −",
            (0, 1) => " (forward)",
            (0, 2) => " (back)",
            (0, 0) => " (toggle)",
            _ => $" {a:X2} {b:X2}",
        };
        return what + dir;
    }

    /// <summary>Human description of a key action, in the active UI language (see KeyAction.Describe in Core).</summary>
    public static string Describe(KeyAction a)
    {
        int mic = MicKeys.IndexOf(a);
        if (mic >= 0)
            return MicKeys.Name(MicKeys.All[mic].Kind);
        if (!IsEnglish)
            return a.Describe();
        switch (a.Kind)
        {
            case KeyActionType.Keyboard:
            {
                if (a == KeyAction.Disabled)
                    return "Disabled";
                var parts = new List<string>();
                foreach (Modifiers m in Enum.GetValues<Modifiers>())
                    if (m != Modifiers.None && a.Mods.HasFlag(m))
                        parts.Add(m.ToString());
                if (a.B != 0)
                    parts.Add(HidUsage.Name(a.B));
                if (a.C != 0)
                    parts.Add(HidUsage.Name(a.C));
                return string.Join(" + ", parts);
            }
            case KeyActionType.Mouse:
                return a.A switch { 1 => "Mouse L", 2 => "Mouse R", 3 => "Mouse M", 4 => "Mouse back", 5 => "Mouse forward", _ => $"Mouse {a.A}" };
            case KeyActionType.Consumer:
                return ConsumerName(a.ConsumerUsage);
            case KeyActionType.Macro:
            {
                string how = (MacroRepeat)a.A switch
                {
                    MacroRepeat.WhileHeld => "while held",
                    MacroRepeat.UntilAnyKey => "until any key pressed",
                    _ => $"{a.B} times",
                };
                return $"Macro {a.C + 1} ({how})";
            }
            case KeyActionType.Function:
                return FunctionName(a.C);
            case KeyActionType.Lighting:
                return LightingName(a.A, a.B, a.C);
            default:
                return a.Describe();
        }
    }
}
