using Windows.System;

namespace AulaKb.App.Controls;

/// <summary>Windows virtual-key to HID keyboard usage, for macro recording.</summary>
static class VirtualKeyMap
{
    static readonly Dictionary<VirtualKey, byte> Map = Build();

    public static bool TryGet(VirtualKey key, out byte usage) => Map.TryGetValue(key, out usage);

    static Dictionary<VirtualKey, byte> Build()
    {
        var m = new Dictionary<VirtualKey, byte>();
        for (int i = 0; i < 26; i++)
            m[VirtualKey.A + i] = (byte)(0x04 + i);
        for (int i = 1; i <= 9; i++)
            m[VirtualKey.Number0 + i] = (byte)(0x1E + i - 1);
        m[VirtualKey.Number0] = 0x27;
        for (int i = 0; i < 12; i++)
            m[VirtualKey.F1 + i] = (byte)(0x3A + i);
        for (int i = 1; i <= 9; i++)
            m[VirtualKey.NumberPad0 + i] = (byte)(0x59 + i - 1);
        m[VirtualKey.NumberPad0] = 0x62;
        m[VirtualKey.Enter] = 0x28;
        m[VirtualKey.Escape] = 0x29;
        m[VirtualKey.Back] = 0x2A;
        m[VirtualKey.Tab] = 0x2B;
        m[VirtualKey.Space] = 0x2C;
        m[VirtualKey.CapitalLock] = 0x39;
        m[VirtualKey.Snapshot] = 0x46;
        m[VirtualKey.Scroll] = 0x47;
        m[VirtualKey.Pause] = 0x48;
        m[VirtualKey.Insert] = 0x49;
        m[VirtualKey.Home] = 0x4A;
        m[VirtualKey.PageUp] = 0x4B;
        m[VirtualKey.Delete] = 0x4C;
        m[VirtualKey.End] = 0x4D;
        m[VirtualKey.PageDown] = 0x4E;
        m[VirtualKey.Right] = 0x4F;
        m[VirtualKey.Left] = 0x50;
        m[VirtualKey.Down] = 0x51;
        m[VirtualKey.Up] = 0x52;
        m[VirtualKey.NumberKeyLock] = 0x53;
        m[VirtualKey.Divide] = 0x54;
        m[VirtualKey.Multiply] = 0x55;
        m[VirtualKey.Subtract] = 0x56;
        m[VirtualKey.Add] = 0x57;
        m[VirtualKey.Decimal] = 0x63;
        m[VirtualKey.Application] = 0x65;
        m[VirtualKey.Control] = 0xE0;
        m[VirtualKey.LeftControl] = 0xE0;
        m[VirtualKey.Shift] = 0xE1;
        m[VirtualKey.LeftShift] = 0xE1;
        m[VirtualKey.Menu] = 0xE2;
        m[VirtualKey.LeftMenu] = 0xE2;
        m[VirtualKey.LeftWindows] = 0xE3;
        m[VirtualKey.RightControl] = 0xE4;
        m[VirtualKey.RightShift] = 0xE5;
        m[VirtualKey.RightMenu] = 0xE6;
        m[VirtualKey.RightWindows] = 0xE7;
        // OEM keys by their US-layout position.
        m[(VirtualKey)0xBD] = 0x2D; // -
        m[(VirtualKey)0xBB] = 0x2E; // =
        m[(VirtualKey)0xDB] = 0x2F; // [
        m[(VirtualKey)0xDD] = 0x30; // ]
        m[(VirtualKey)0xDC] = 0x31; // \
        m[(VirtualKey)0xBA] = 0x33; // ;
        m[(VirtualKey)0xDE] = 0x34; // '
        m[(VirtualKey)0xC0] = 0x35; // `
        m[(VirtualKey)0xBC] = 0x36; // ,
        m[(VirtualKey)0xBE] = 0x37; // .
        m[(VirtualKey)0xBF] = 0x38; // /
        m[(VirtualKey)0xE2] = 0x64; // ISO <>
        return m;
    }
}
