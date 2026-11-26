using AulaKb.Core.Hid;

namespace AulaKb.Core;

/// <summary>
/// 2.4G receiver (3554:FA09): 20-byte output/input report 0x13 on the collection with usage
/// page 0xFF02, usage 2. The same settings blocks as the cable travel in 14-byte fragments,
/// each echoed by the keyboard. Only the profile has a known read command; the other blocks
/// are cached from the factory tables and updated on write, so the UI can still work.
///
/// Commands come from the F87 control projects and the decompiled AULA driver. Firmware and
/// reset commands are deliberately absent, just as on the cable.
/// </summary>
public sealed class DongleKeyboard : AulaKeyboard
{
    const byte ReportId = 0x13;
    const int DongleReportLength = 20;
    const int FragmentBytes = 14;

    const byte CmdWriteMatrix = 0x01;   // sub = layer
    const byte CmdWritePerKey = 0x02;   // sub = 0x1C
    const byte CmdWriteMacros = 0x03;   // sub = page
    const byte CmdWriteProfile = 0x04;  // sub = 0x0A
    const byte CmdWritePalette = 0x09;  // sub = 0x25
    const byte CmdSave = 0x0A;          // sub = 1, data 04 07; no echo
    const byte CmdReadMatrix = 0x41;
    const byte CmdReadProfile = 0x44;
    const byte CmdReadPalette = 0x49;
    const byte CmdReadMacros = 0x43;
    const byte CmdLive = 0x88;          // no echo

    static readonly HashSet<byte> AllowedCommands =
    [
        CmdWriteMatrix, CmdWritePerKey, CmdWriteMacros, CmdWriteProfile, CmdWritePalette, CmdSave,
        CmdReadProfile, CmdLive,
    ];

    readonly IReportDevice _device;
    readonly object _io = new();

    LightProfile? _profile;
    Palette? _palette;
    PerKeyColors? _perKey;
    readonly Dictionary<Layer, KeyLayer> _layers = [];
    IReadOnlyList<Macro> _macros = [];

    internal DongleKeyboard(HidDeviceInfo info, IReportDevice device) : base(info) => _device = device;

    public override string FirmwareVersion => "n/a over 2.4G";

    public override string TransportName => "2.4G receiver";

    public override double FrameIntervalMs => 70; // the radio manages ~12-20 live frames per second

    public override bool CanReadBlocks => false;

    public override KeyboardIdentity QueryIdentity() => new([], Wireless: true);

    protected override byte[] ReadRaw(Block block, byte sub)
    {
        // A sleeping keyboard answers nothing, so never block the UI on an unavailable read.
        return block switch
        {
            Block.Profile => ReadProfileBlock().ToArray(),
            Block.Palette => (_palette ??= Defaults.Palette()).ToArray(),
            Block.PerKey => (_perKey ??= Defaults.PerKeyColors()).ToArray(),
            Block.Matrix => CacheLayer((Layer)sub).ToArray(),
            Block.Macro => Macro.BuildBlob(_macros),
            _ => [],
        };
    }

    protected override void WriteRaw(Block block, byte sub, ReadOnlySpan<byte> data)
    {
        lock (_io)
        {
            switch (block)
            {
                case Block.Profile:
                    SendAll(Fragments(CmdWriteProfile, 0x0A, data), echo: true);
                    _profile = new LightProfile(data.ToArray());
                    break;
                case Block.Palette:
                    SendAll(Fragments(CmdWritePalette, 0x25, data), echo: true);
                    _palette = new Palette(data.ToArray());
                    break;
                case Block.PerKey:
                    // 378 bytes of colours plus the 00 00 5A A5 00 00 trailer = 384.
                    SendAll(Fragments(CmdWritePerKey, 0x1C, data), echo: true);
                    _perKey = new PerKeyColors(data.ToArray());
                    break;
                case Block.Matrix:
                    SendAll(Fragments(CmdWriteMatrix, sub, data), echo: true);
                    _layers[(Layer)sub] = new KeyLayer(data.ToArray());
                    break;
                case Block.Macro:
                    SendAll(Fragments(CmdWriteMacros, sub, data), echo: true);
                    _macros = Macro.ParseBlob(data);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(block));
            }
            Save();
        }
    }

    public override void SendDirectFrame(ReadOnlySpan<byte> rgb)
    {
        if (rgb.Length != LedCount * 3)
            throw new ArgumentException($"Expected {LedCount * 3} bytes, got {rgb.Length}.", nameof(rgb));

        // The receiver's audio-dance format sends groups of equally coloured LEDs, largest
        // group first. Every distinct colour costs bytes, so cap at 14 fragments of 14 bytes.
        var groups = new List<(byte R, byte G, byte B, List<byte> Leds)>();
        for (int led = 0; led < LedCount; led++)
        {
            byte r = rgb[led * 3], g = rgb[led * 3 + 1], b = rgb[led * 3 + 2];
            if (r == 0 && g == 0 && b == 0)
                continue;
            var group = groups.FirstOrDefault(x => x.R == r && x.G == g && x.B == b);
            if (group.Leds is null)
            {
                group = (r, g, b, []);
                groups.Add(group);
            }
            group.Leds.Add((byte)led);
        }

        List<byte> body = [];
        foreach (var group in groups.OrderByDescending(g => g.Leds.Count))
        {
            if (body.Count + 4 + group.Leds.Count > FragmentBytes * 14)
                break;
            body.Add(group.R);
            body.Add(group.G);
            body.Add(group.B);
            body.Add((byte)group.Leds.Count);
            body.AddRange(group.Leds);
        }

        List<byte[]> frames;
        if (body.Count == 0)
        {
            frames = [Frame(CmdLive, 1, 0, 0x23, [])]; // captured all-off frame
        }
        else
        {
            var chunks = body.Chunk(FragmentBytes).ToList();
            frames = chunks.Select((chunk, i) =>
                Frame(CmdLive, (byte)chunks.Count, (byte)i, (byte)(0x10 + chunk.Length), chunk)).ToList();
        }

        lock (_io)
        {
            foreach (var frame in frames)
            {
                if (!_device.WriteReport(frame))
                    throw new IOException("2.4G receiver refused a live frame.");
                Thread.Sleep(10); // 2 ms stutters; 10 ms is smooth
            }
        }
    }

    KeyLayer CacheLayer(Layer layer)
    {
        if (!_layers.TryGetValue(layer, out var keys))
        {
            keys = Defaults.Layer(layer);
            _layers[layer] = keys;
        }
        return keys;
    }

    byte[] ReadProfileBlock()
    {
        lock (_io)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Drain();
                if (!_device.WriteReport(Frame(CmdReadProfile, 1, 0, 0, [])))
                    throw new IOException("2.4G receiver refused the profile read.");

                var parts = new byte[10][];
                long deadline = Environment.TickCount64 + 1200;
                while (parts.Any(p => p is null) && Environment.TickCount64 < deadline)
                {
                    var buffer = new byte[DongleReportLength];
                    int n = _device.ReadReport(buffer, 60);
                    if (n < DongleReportLength || buffer[0] != ReportId || buffer[1] != CmdReadProfile || buffer[2] != 0x0A)
                        continue;
                    int seq = buffer[3];
                    if (seq >= parts.Length)
                        continue;
                    int length = Math.Min(buffer[4] & 0x0F, FragmentBytes);
                    parts[seq] = buffer.AsSpan(5, length).ToArray();
                }

                if (parts.All(p => p is not null))
                {
                    byte[] block = parts.SelectMany(p => p!).ToArray();
                    if (block.Length < LightProfile.Length)
                        return [.. block, .. new byte[LightProfile.Length - block.Length]];
                    return block.AsSpan(0, LightProfile.Length).ToArray();
                }
            }
            throw new IOException("The 2.4G keyboard is not answering. Press a key to wake it, then retry.");
        }
    }

    void SendAll(IReadOnlyList<byte[]> frames, bool echo)
    {
        Drain();
        foreach (var frame in frames)
        {
            if (!AllowedCommands.Contains(frame[1]))
                throw new InvalidOperationException($"Command 0x{frame[1]:X2} is not allowed.");
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (!_device.WriteReport(frame))
                    throw new IOException($"2.4G receiver refused command 0x{frame[1]:X2}.");
                if (!echo || WaitEcho(frame, 250))
                    break;
                if (attempt == 2)
                    throw new IOException("The 2.4G keyboard stopped answering. Press a key to wake it, then retry.");
                Thread.Sleep(30);
            }
            Thread.Sleep(12); // the vendor tool spaces radio packets ~10-15 ms apart
        }
    }

    bool WaitEcho(byte[] frame, int timeoutMs)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var buffer = new byte[DongleReportLength];
            int n = _device.ReadReport(buffer, 50);
            if (n < DongleReportLength || buffer[0] != ReportId)
                continue;
            if (buffer[1] == frame[1] && buffer[2] == frame[2] && buffer[3] == frame[3])
                return true;
        }
        return false;
    }

    void Drain()
    {
        var buffer = new byte[DongleReportLength];
        while (_device.ReadReport(buffer, 5) == DongleReportLength)
        {
        }
    }

    void Save()
    {
        // The F87 Pro does not echo this frame.
        SendAll([Frame(CmdSave, 1, 0, 2, [0x04, 0x07])], echo: false);
        Thread.Sleep(80);
    }

    static IReadOnlyList<byte[]> Fragments(byte command, byte sub, ReadOnlySpan<byte> data)
    {
        var result = new List<byte[]>();
        for (int offset = 0; offset < data.Length; offset += FragmentBytes)
        {
            int count = Math.Min(FragmentBytes, data.Length - offset);
            result.Add(Frame(command, sub, (byte)result.Count, (byte)count, data.Slice(offset, count)));
        }
        return result;
    }

    static byte[] Frame(byte command, byte sub, byte sequence, byte length, ReadOnlySpan<byte> data)
    {
        if (data.Length > FragmentBytes)
            throw new ArgumentException($"A 2.4G frame holds at most {FragmentBytes} data bytes.");
        var frame = new byte[DongleReportLength];
        frame[0] = ReportId;
        frame[1] = command;
        frame[2] = sub;
        frame[3] = sequence;
        frame[4] = length;
        data.CopyTo(frame.AsSpan(5));
        for (int i = 0; i < DongleReportLength - 1; i++)
            frame[DongleReportLength - 1] += frame[i];
        return frame;
    }

    public override void Dispose() => _device.Dispose();
}
