namespace AulaKb.Core;

/// <summary>Saves and restores every persistent table as plain files.</summary>
public static class KeyboardBackup
{
    public static void Save(AulaKeyboard kb, string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "profile.bin"), kb.ReadProfile().ToArray());
        File.WriteAllBytes(Path.Combine(dir, "palette.bin"), kb.ReadPalette().ToArray());
        File.WriteAllBytes(Path.Combine(dir, "perkey.bin"), kb.ReadPerKeyColors().ToArray());
        foreach (Layer layer in Enum.GetValues<Layer>())
            File.WriteAllBytes(Path.Combine(dir, $"layer_{layer}.bin"), kb.ReadLayer(layer).ToArray());
        File.WriteAllBytes(Path.Combine(dir, "macros.bin"), Macro.BuildBlob(kb.ReadMacros()));
    }

    /// <summary>Validates every file before writing anything.</summary>
    public static void Restore(AulaKeyboard kb, string dir)
    {
        var profile = new LightProfile(File.ReadAllBytes(Path.Combine(dir, "profile.bin")));
        if (!profile.IsValid)
            throw new InvalidDataException("profile.bin: missing 5A A5 trailer");
        var palette = new Palette(File.ReadAllBytes(Path.Combine(dir, "palette.bin")));
        var perKey = new PerKeyColors(File.ReadAllBytes(Path.Combine(dir, "perkey.bin")));
        var layers = new Dictionary<Layer, KeyLayer>();
        foreach (Layer layer in Enum.GetValues<Layer>())
        {
            var keys = new KeyLayer(File.ReadAllBytes(Path.Combine(dir, $"layer_{layer}.bin")));
            if (!keys.IsValid)
                throw new InvalidDataException($"layer_{layer}.bin: missing 5A A5 trailer");
            layers[layer] = keys;
        }
        string macroPath = Path.Combine(dir, "macros.bin");
        var macros = File.Exists(macroPath) ? Macro.ParseBlob(File.ReadAllBytes(macroPath)) : [];

        foreach (var (layer, keys) in layers)
            kb.WriteLayer(layer, keys);
        kb.WriteProfile(profile);
        kb.WritePalette(palette);
        kb.WritePerKeyColors(perKey);
        kb.WriteMacros(macros);
    }
}
