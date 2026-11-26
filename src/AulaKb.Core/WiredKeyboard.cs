using AulaKb.Core.Hid;

namespace AulaKb.Core;

/// <summary>
/// Cable transport (258A:010C): vendor configuration collection, usage page 0xFF00,
/// 520-byte feature report id 6. The frame and command whitelist come from the vendor tool,
/// captured through a hid.dll proxy on firmware "wired" (model 0xA9).
/// </summary>
public sealed class WiredKeyboard : AulaKeyboard
{
    const byte ReportId = 6;

    /// <summary>Firmware/ISP paths (report id 5, 0x88/0x89 code windows, bootloader) stay absent:
    /// a wrong write there can brick the keyboard.</summary>
    static readonly HashSet<byte> AllowedCommands =
    [
        Cmd.WriteMatrix, Cmd.WriteProfile, Cmd.WriteMacros, Cmd.WritePerKey, Cmd.DirectFrame, Cmd.WritePalette,
        Cmd.ReadIdentity, Cmd.ReadMatrix, Cmd.ReadProfile, Cmd.ReadMacros, Cmd.ReadPerKey, Cmd.ReadPalette,
    ];

    readonly IFeatureDevice _device;
    readonly object _io = new();

    internal WiredKeyboard(HidDeviceInfo info, IFeatureDevice device) : base(info) => _device = device;

    public override string FirmwareVersion => $"V{Info.VersionNumber >> 8}.{(Info.VersionNumber >> 4) & 0xF}.{Info.VersionNumber & 0xF}";

    public override string TransportName => "USB cable";

    protected override byte[] ReadRaw(Block block, byte sub)
    {
        var (command, length) = block switch
        {
            Block.Identity => (Cmd.ReadIdentity, 6),
            Block.Profile => (Cmd.ReadProfile, LightProfile.Length),
            Block.Palette => (Cmd.ReadPalette, Palette.Length),
            Block.PerKey => (Cmd.ReadPerKey, PerKeyColors.Length),
            Block.Matrix => (Cmd.ReadMatrix, KeyLayer.Length),
            Block.Macro => (Cmd.ReadMacros, ChunkLength),
            _ => throw new ArgumentOutOfRangeException(nameof(block)),
        };
        return Read(command, sub, length);
    }

    protected override void WriteRaw(Block block, byte sub, ReadOnlySpan<byte> data)
    {
        byte command = block switch
        {
            Block.Profile => Cmd.WriteProfile,
            Block.Palette => Cmd.WritePalette,
            Block.PerKey => Cmd.WritePerKey,
            Block.Matrix => Cmd.WriteMatrix,
            Block.Macro => Cmd.WriteMacros,
            _ => throw new ArgumentOutOfRangeException(nameof(block)),
        };
        Send(BuildFrame(command, sub, data.Length, data), pauseAfter: true);
    }

    public override void SendDirectFrame(ReadOnlySpan<byte> rgb)
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

    public override void Dispose() => _device.Dispose();
}
