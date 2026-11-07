using System.Runtime.InteropServices;
using AulaKb.Core;
using NAudio.CoreAudioApi;

namespace AulaKb.App;

/// <summary>
/// Microphone keys. The keyboard's media (consumer page) codes have no microphone mute or
/// microphone volume, so such a key is stored as a rare combination (Ctrl+Shift+Alt+F10/F11/F12)
/// and AulaKb, running in the tray, catches it as a global hotkey and changes the default
/// capture device itself. Works only while AulaKb runs.
/// </summary>
static class MicKeys
{
    const Modifiers Mods = Modifiers.LCtrl | Modifiers.LShift | Modifiers.LAlt;
    const uint ModAll = 0x0001 /* ALT */ | 0x0002 /* CONTROL */ | 0x0004 /* SHIFT */ | 0x4000 /* NOREPEAT */;
    const int FirstId = 0xA100;

    public enum Kind { Mute, Up, Down }

    /// <summary>The key action written to the keyboard and the virtual key Windows reports for it.</summary>
    public static readonly (Kind Kind, KeyAction Action, uint Vk)[] All =
    [
        (Kind.Mute, KeyAction.Key(0x43, Mods), 0x79), // F10
        (Kind.Up, KeyAction.Key(0x44, Mods), 0x7A),   // F11
        (Kind.Down, KeyAction.Key(0x45, Mods), 0x7B), // F12
    ];

    public static int IndexOf(KeyAction action) => Array.FindIndex(All, m => m.Action == action);

    public static string Name(Kind kind) => Loc.S(kind switch
    {
        Kind.Mute => "media.mic_mute",
        Kind.Up => "media.mic_up",
        _ => "media.mic_down",
    });

    public static void Register(IntPtr hwnd)
    {
        for (int i = 0; i < All.Length; i++)
            RegisterHotKey(hwnd, FirstId + i, ModAll, All[i].Vk);
    }

    public static void Unregister(IntPtr hwnd)
    {
        for (int i = 0; i < All.Length; i++)
            UnregisterHotKey(hwnd, FirstId + i);
    }

    /// <summary>Handles a WM_HOTKEY id; false when it is not one of ours.</summary>
    public static bool Handle(int id)
    {
        int index = id - FirstId;
        if (index < 0 || index >= All.Length)
            return false;
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var mic = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            var volume = mic.AudioEndpointVolume;
            switch (All[index].Kind)
            {
                case Kind.Mute:
                    volume.Mute = !volume.Mute;
                    break;
                case Kind.Up:
                    volume.Mute = false;
                    volume.MasterVolumeLevelScalar = Math.Min(1f, volume.MasterVolumeLevelScalar + 0.05f);
                    break;
                case Kind.Down:
                    volume.MasterVolumeLevelScalar = Math.Max(0f, volume.MasterVolumeLevelScalar - 0.05f);
                    break;
            }
        }
        catch (Exception)
        {
            // no microphone: nothing to do
        }
        return true;
    }

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
