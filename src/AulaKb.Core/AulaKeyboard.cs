using AulaKb.Core.Hid;

namespace AulaKb.Core;

/// <summary>
/// An Aula F87 Pro, either on the USB cable (520-byte feature reports, 258A:010C) or on the
/// 2.4G receiver (20-byte output reports, 3554:FA09). Both transports carry the same settings
/// blocks; the receiver can only read the profile, so the other blocks are cached locally.
/// </summary>
public abstract class AulaKeyboard : IDisposable
{
    public const ushort VendorId = 0x258A;
    public const ushort ProductId = 0x010C;
    public const ushort DongleVendorId = 0x3554;
    public const ushort DongleProductId = 0xFA09;
    public const int ReportLength = 520;
    public const int ChunkLength = 512;
    public const int LedCount = 126;

    protected AulaKeyboard(HidDeviceInfo info) => Info = info;

    public HidDeviceInfo Info { get; }

    /// <summary>Shown in Settings; the receiver reports its own bcdDevice, not the keyboard firmware.</summary>
    public abstract string FirmwareVersion { get; }

    /// <summary>Shown on the Device page: "USB cable" or "2.4G receiver".</summary>
    public abstract string TransportName { get; }

    /// <summary>Spacing of live frames; the radio is much slower than the cable.</summary>
    public virtual double FrameIntervalMs => 46.8;

    /// <summary>False over 2.4G, where only the profile can be read back.</summary>
    public virtual bool CanReadBlocks => true;

    /// <summary>Settings blocks shared by both transports.</summary>
    protected enum Block { Identity, Profile, Palette, PerKey, Matrix, Macro }

    protected abstract byte[] ReadRaw(Block block, byte sub);

    protected abstract void WriteRaw(Block block, byte sub, ReadOnlySpan<byte> data);

    /// <summary>Real-time colours, never stored. RGB is 126 LEDs × 3 bytes.</summary>
    public abstract void SendDirectFrame(ReadOnlySpan<byte> rgb);

    /// <summary>Prefer the cable when both are plugged in; fall back to the 2.4G receiver.</summary>
    public static AulaKeyboard? Find()
    {
        AulaKeyboard? wired = FindWired();
        return wired ?? FindDongle();
    }

    public static WiredKeyboard? FindWired()
    {
        var info = WindowsHid.Enumerate(VendorId).FirstOrDefault(d =>
            d.ProductId == ProductId && d.UsagePage == 0xFF00 && d.FeatureLength == ReportLength);
        return info is null ? null : new WiredKeyboard(info, WindowsHid.Open(info.Path));
    }

    public static DongleKeyboard? FindDongle()
    {
        var info = WindowsHid.Enumerate(DongleVendorId).FirstOrDefault(d =>
            d.ProductId == DongleProductId && d.UsagePage == 0xFF02 && d.InputLength == 20 && d.OutputLength == 20);
#pragma warning disable CA1416 // this library's HID transport is Windows-only by design
        return info is null ? null : new DongleKeyboard(info, new WindowsHidStream(info.Path));
#pragma warning restore CA1416
    }

    public virtual KeyboardIdentity QueryIdentity() => new(ReadRaw(Block.Identity, 1));

    public LightProfile ReadProfile() => new(ReadRaw(Block.Profile, 0));

    public Palette ReadPalette() => new(ReadRaw(Block.Palette, 0));

    public PerKeyColors ReadPerKeyColors() => new(ReadRaw(Block.PerKey, 0));

    public KeyLayer ReadLayer(Layer layer) => new(ReadRaw(Block.Matrix, (byte)layer));

    public IReadOnlyList<Macro> ReadMacros() => Macro.ParseBlob(ReadRaw(Block.Macro, 0));

    public void WriteProfile(LightProfile profile) => WriteRaw(Block.Profile, 0, profile.ToArray());

    public void WritePalette(Palette palette) => WriteRaw(Block.Palette, 0, palette.ToArray());

    public void WritePerKeyColors(PerKeyColors colors) => WriteRaw(Block.PerKey, 0, colors.ToArray());

    public void WriteLayer(Layer layer, KeyLayer keys) => WriteRaw(Block.Matrix, (byte)layer, keys.ToArray());

    public void WriteMacros(IReadOnlyList<Macro> macros)
    {
        byte[] blob = Macro.BuildBlob(macros);
        if (blob.Length > ChunkLength)
            throw new InvalidOperationException($"Macros need {blob.Length} bytes; only {ChunkLength} are supported.");
        WriteRaw(Block.Macro, 0, blob);
    }

    /// <summary>Writes the vendor tool's factory tables (what its Reset button sends).</summary>
    public void RestoreFactoryDefaults()
    {
        foreach (Layer layer in Enum.GetValues<Layer>())
            WriteLayer(layer, Defaults.Layer(layer));
        WriteProfile(Defaults.Profile());
        WritePalette(Defaults.Palette());
    }

    public abstract void Dispose();
}

/// <summary>Configuration commands (report id 6). Reads are the write code | 0x80.</summary>
public static class Cmd
{
    public const byte WriteMatrix = 0x03;   // sub = layer, 512 bytes
    public const byte WriteProfile = 0x04;  // 128 bytes
    public const byte WriteMacros = 0x05;   // macro blob
    public const byte WritePerKey = 0x06;   // 384 bytes, planar R/G/B x 126
    public const byte DirectFrame = 0x08;   // 378 bytes, RGB per LED, not stored
    public const byte WritePalette = 0x0A;  // 512 bytes, 7 colours per effect
    public const byte ReadIdentity = 0x82;
    public const byte ReadMatrix = 0x83;
    public const byte ReadProfile = 0x84;
    public const byte ReadMacros = 0x85;
    public const byte ReadPerKey = 0x86;
    public const byte ReadPalette = 0x8A;
}

/// <summary>Reply of the 0x82 identity query, e.g. 03 00 00 00 00 A9.</summary>
public sealed record KeyboardIdentity(byte[] Raw, bool Wireless = false)
{
    public byte Variant => Raw.Length > 4 ? Raw[4] : (byte)0;

    public byte Model => Raw.Length > 5 ? Raw[5] : (byte)0;

    public string RawHex => Wireless ? "not available over 2.4G" : string.Join(' ', Raw.Select(b => b.ToString("X2")));

    /// <summary>Names from the vendor tool's KB.ini "Psd" signatures.</summary>
    public string Name => Wireless ? "AULA F87 PRO (2.4G)" : (Variant, Model) switch
    {
        (0, 0xA9) => "AULA F87 PRO Wired",
        (1, 0x0B) => "AULA F87 PRO",
        (0, 0x8F) => "AULA F87",
        (3, 0x66) => "AULA F87 PRO (revision 0x66, not in the vendor tool)",
        _ => $"Unknown Aula keyboard (model 0x{Model:X2})",
    };
}
