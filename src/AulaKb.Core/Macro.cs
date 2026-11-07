using System.Text;

namespace AulaKb.Core;

/// <summary>One recorded step: key down/up followed by a delay in milliseconds.</summary>
public readonly record struct MacroEvent(bool Down, byte Usage, ushort DelayMs);

/// <summary>
/// Macro record as written by 0x05:
///   [event count LE16][bytes after this header LE16][name byte length][name UTF-16LE]
///   then 4 bytes per event: [00 down / 80 up][delay hi][delay lo][HID usage].
/// Observed for a single macro. Several macros are assumed to follow one another in the
/// same blob, with the key assignment's last byte selecting the macro by position
/// (inferred, read back to verify).
/// </summary>
public sealed class Macro
{
    public string Name { get; set; } = "Macro";

    public List<MacroEvent> Events { get; } = [];

    public byte[] ToBytes()
    {
        byte[] name = Encoding.Unicode.GetBytes(Name);
        if (name.Length > 64)
            name = name.AsSpan(0, 64).ToArray();
        int body = 1 + name.Length + 4 * Events.Count;
        var buf = new byte[4 + body];
        buf[0] = (byte)Events.Count;
        buf[1] = (byte)(Events.Count >> 8);
        buf[2] = (byte)body;
        buf[3] = (byte)(body >> 8);
        buf[4] = (byte)name.Length;
        name.CopyTo(buf, 5);
        int i = 5 + name.Length;
        foreach (var e in Events)
        {
            buf[i++] = e.Down ? (byte)0x00 : (byte)0x80;
            buf[i++] = (byte)(e.DelayMs >> 8);
            buf[i++] = (byte)e.DelayMs;
            buf[i++] = e.Usage;
        }
        return buf;
    }

    public static byte[] BuildBlob(IReadOnlyList<Macro> macros) => macros.SelectMany(m => m.ToBytes()).ToArray();

    public static IReadOnlyList<Macro> ParseBlob(ReadOnlySpan<byte> blob)
    {
        var list = new List<Macro>();
        int pos = 0;
        while (pos + 5 <= blob.Length)
        {
            int count = blob[pos] | (blob[pos + 1] << 8);
            int body = blob[pos + 2] | (blob[pos + 3] << 8);
            if (count == 0 || body == 0 || pos + 4 + body > blob.Length)
                break;
            int nameLen = blob[pos + 4];
            if (5 + nameLen + 4 * count != 4 + body)
                break;
            var macro = new Macro { Name = Encoding.Unicode.GetString(blob.Slice(pos + 5, nameLen)) };
            int i = pos + 5 + nameLen;
            for (int n = 0; n < count; n++, i += 4)
                macro.Events.Add(new MacroEvent(blob[i] == 0x00, blob[i + 3], (ushort)((blob[i + 1] << 8) | blob[i + 2])));
            list.Add(macro);
            pos += 4 + body;
        }
        return list;
    }
}
