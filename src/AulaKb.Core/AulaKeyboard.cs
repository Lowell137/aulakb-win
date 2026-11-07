using AulaKb.Core.Hid;

namespace AulaKb.Core;

/// <summary>
/// Aula F87 Pro (SinoWealth, 258A:010C) over its vendor configuration collection
/// (usage page 0xFF00, 520-byte feature report id 6).
///
/// Frame: [0]=report id 6, [1]=command, [2]=sub command, [3]=0, [4]=chunk count,
/// [5]=chunk index, [6..7]=payload length (LE), [8..519]=payload.
/// Reads: send command|0x80 with the wanted length, then GetFeature; the keyboard
/// echoes the header and returns data from byte 8.
///
/// Every command below was observed from the vendor tool (OemDrv 2.1) through a
/// hid.dll capture proxy, firmware "wired" (model 0xA9).
/// </summary>
public sealed class AulaKeyboard : IDisposable
{
    public const ushort VendorId = 0x258A;
    public const ushort ProductId = 0x010C;
    public const int ReportLength = 520;
    public const int ChunkLength = 512;
    public const int LedCount = 126;
    const ushort ConfigUsagePage = 0xFF00;
    const byte ReportId = 6;

    /// <summary>
    /// Commands this library may send. Firmware/ISP paths (report id 5, 0x88/0x89 code
    /// windows, bootloader, updates) are deliberately absent: a wrong write there can brick
    /// the keyboard.
    /// </summary>
    static readonly HashSet<byte> AllowedCommands =
    [
        Cmd.WriteMatrix, Cmd.WriteProfile, Cmd.WriteMacros, Cmd.WritePerKey, Cmd.DirectFrame, Cmd.WritePalette,
        Cmd.ReadIdentity, Cmd.ReadMatrix, Cmd.ReadProfile, Cmd.ReadMacros, Cmd.ReadPerKey, Cmd.ReadPalette,
    ];

    readonly IFeatureDevice _device;
    readonly object _io = new();

    AulaKeyboard(HidDeviceInfo info, IFeatureDevice device)
    {
        Info = info;
        _device = device;
    }

    public HidDeviceInfo Info { get; }

    /// <summary>Firmware version as shown by the vendor tool (USB bcdDevice, e.g. 0x0200 = V2.0.0).</summary>
    public string FirmwareVersion => $"V{Info.VersionNumber >> 8}.{(Info.VersionNumber >> 4) & 0xF}.{Info.VersionNumber & 0xF}";

    /// <summary>Finds the wired keyboard's configuration interface, or null if it is not connected.</summary>
    public static AulaKeyboard? Find()
    {
        var info = WindowsHid.Enumerate(VendorId).FirstOrDefault(d =>
            d.ProductId == ProductId && d.UsagePage == ConfigUsagePage && d.FeatureLength == ReportLength);
        return info is null ? null : new AulaKeyboard(info, WindowsHid.Open(info.Path));
    }

    // ---- reads ----

    public KeyboardIdentity QueryIdentity() => new(Read(Cmd.ReadIdentity, 1, 6));

    public LightProfile ReadProfile() => new(Read(Cmd.ReadProfile, 0, LightProfile.Length));

    public Palette ReadPalette() => new(Read(Cmd.ReadPalette, 0, Palette.Length));

    public PerKeyColors ReadPerKeyColors() => new(Read(Cmd.ReadPerKey, 0, PerKeyColors.Length));

    public KeyLayer ReadLayer(Layer layer) => new(Read(Cmd.ReadMatrix, (byte)layer, KeyLayer.Length));

    public IReadOnlyList<Macro> ReadMacros() => Macro.ParseBlob(Read(Cmd.ReadMacros, 0, ChunkLength));

    // ---- writes (all persistent on the keyboard) ----

    public void WriteProfile(LightProfile profile) => Write(Cmd.WriteProfile, 0, profile.ToArray());

    public void WritePalette(Palette palette) => Write(Cmd.WritePalette, 0, palette.ToArray());

    public void WritePerKeyColors(PerKeyColors colors) => Write(Cmd.WritePerKey, 0, colors.ToArray());

    public void WriteLayer(Layer layer, KeyLayer keys) => Write(Cmd.WriteMatrix, (byte)layer, keys.ToArray());

    public void WriteMacros(IReadOnlyList<Macro> macros)
    {
        byte[] blob = Macro.BuildBlob(macros);
        if (blob.Length > ChunkLength)
            throw new InvalidOperationException($"Macros need {blob.Length} bytes; only {ChunkLength} are supported.");
        Write(Cmd.WriteMacros, 0, blob);
    }

    /// <summary>Writes the vendor tool's factory tables (what its Reset button sends).</summary>
    public void RestoreFactoryDefaults()
    {
        foreach (Layer layer in Enum.GetValues<Layer>())
            WriteLayer(layer, Defaults.Layer(layer));
        WriteProfile(Defaults.Profile());
        WritePalette(Defaults.Palette());
    }

    /// <summary>
    /// Real-time colours (0x08), LED-index order, RGB per LED. Nothing is stored on the
    /// keyboard: it falls back to its own effect a few hundred ms after frames stop,
    /// so callers must resend about every 50 ms.
    /// </summary>
    public void SendDirectFrame(ReadOnlySpan<byte> rgb)
    {
        if (rgb.Length != LedCount * 3)
            throw new ArgumentException($"Expected {LedCount * 3} bytes, got {rgb.Length}.", nameof(rgb));
        Send(BuildFrame(Cmd.DirectFrame, 0, rgb.Length, rgb), pauseAfter: false);
    }

    byte[] Read(byte command, byte sub, int length)
    {
        lock (_io)
        {
            // The keyboard answers with whatever report it last held until the new reply is
            // ready (e.g. a 0x08 direct frame sent just before), so poll until the command
            // matches and re-send the request once in a while.
            var request = BuildFrame(command, sub, length, ReadOnlySpan<byte>.Empty);
            var response = new byte[ReportLength];
            byte got = 0;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (attempt % 3 == 0)
                    SetWithRetry(request);
                Thread.Sleep(20 + attempt * 15);
                Array.Clear(response);
                response[0] = ReportId;
                if (!_device.GetFeature(response))
                    throw new IOException($"GetFeature failed for command 0x{command:X2}.");
                got = response[1];
                if (got == command)
                    return response.AsSpan(8, length).ToArray();
            }
            throw new IOException($"Unexpected reply: expected command 0x{command:X2}, got 0x{got:X2}.");
        }
    }

    void Write(byte command, byte sub, ReadOnlySpan<byte> payload) =>
        Send(BuildFrame(command, sub, payload.Length, payload), pauseAfter: true);

    void Send(byte[] frame, bool pauseAfter)
    {
        lock (_io)
        {
            SetWithRetry(frame);
            if (pauseAfter)
                Thread.Sleep(30); // the vendor tool spaces persistent writes ~60-80 ms apart
        }
    }

    static byte[] BuildFrame(byte command, byte sub, int length, ReadOnlySpan<byte> payload)
    {
        if (!AllowedCommands.Contains(command))
            throw new InvalidOperationException($"Command 0x{command:X2} is not allowed.");
        if (length > ChunkLength)
            throw new ArgumentOutOfRangeException(nameof(length));

        var frame = new byte[ReportLength];
        frame[0] = ReportId;
        frame[1] = command;
        frame[2] = sub;
        frame[3] = 0;
        frame[4] = 1; // chunk count
        frame[5] = 0; // chunk index
        frame[6] = (byte)(length & 0xFF);
        frame[7] = (byte)(length >> 8);
        payload.CopyTo(frame.AsSpan(8));
        return frame;
    }

    void SetWithRetry(byte[] frame)
    {
        // Same policy as the vendor tool: 3 attempts, 70 ms apart.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (_device.SetFeature(frame))
                return;
            Thread.Sleep(70);
        }
        throw new IOException($"SetFeature failed for command 0x{frame[1]:X2}.");
    }

    public void Dispose() => _device.Dispose();
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
public sealed record KeyboardIdentity(byte[] Raw)
{
    public byte Variant => Raw.Length > 4 ? Raw[4] : (byte)0;

    public byte Model => Raw.Length > 5 ? Raw[5] : (byte)0;

    public string RawHex => string.Join(' ', Raw.Select(b => b.ToString("X2")));

    /// <summary>Names from the vendor tool's KB.ini "Psd" signatures.</summary>
    public string Name => (Variant, Model) switch
    {
        (0, 0xA9) => "AULA F87 PRO Wired",
        (1, 0x0B) => "AULA F87 PRO",
        (0, 0x8F) => "AULA F87",
        (3, 0x66) => "AULA F87 PRO (revision 0x66, not in the vendor tool)",
        _ => $"Unknown Aula keyboard (model 0x{Model:X2})",
    };
}
