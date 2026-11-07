using AulaKb.Core;

// aulakb: command line access to the Aula F87 Pro configuration.
// Read-only commands never write; writing commands say so in their name.

if (args.Length == 0)
{
    Console.WriteLine("""
        usage: aulakb <command>
          info                      identity, firmware, active effect
          verify                    read every table, re-serialize, compare (read only)
          dump                      print profile, macros and key layers (read only)
          backup <dir>              save every table to <dir> (read only)
          restore <dir>             write tables saved by backup
          effect <id> [bri] [spd]   set the hardware effect (0..0x15)
          factory-reset             write the vendor tool's factory tables
        """);
    return 1;
}

using var kb = AulaKeyboard.Find();
if (kb is null)
{
    Console.Error.WriteLine("Aula F87 Pro (258A:010C) not found.");
    return 2;
}

switch (args[0])
{
    case "info":
    {
        var id = kb.QueryIdentity();
        var p = kb.ReadProfile();
        Console.WriteLine($"{id.Name}  identity [{id.RawHex}]  firmware {kb.FirmwareVersion}");
        Console.WriteLine($"effect {Effects.Get(p.Effect).Name} (0x{p.Effect:X2}), brightness {p.GetBrightness(p.Effect)}, speed {p.GetSpeed(p.Effect)}, colour slot {p.GetColourSlot(p.Effect)}");
        Console.WriteLine($"debounce stage {p.Debounce + 1}, tap {(p.TapSensitivity == 0 ? "off" : $"on ({p.TapSensitivity})")}");
        return 0;
    }
    case "verify":
    {
        int bad = 0;
        bad += Check("profile", ReadRaw(() => kb.ReadProfile().ToArray()), r => new LightProfile(r).ToArray(), r => new LightProfile(r).IsValid);
        bad += Check("palette", ReadRaw(() => kb.ReadPalette().ToArray()), r => new Palette(r).ToArray(), r => r[506] == 0x5A && r[507] == 0xA5);
        bad += Check("per-key", ReadRaw(() => kb.ReadPerKeyColors().ToArray()), r => new PerKeyColors(r).ToArray(), _ => true);
        foreach (Layer layer in Enum.GetValues<Layer>())
            bad += Check($"layer {layer}", ReadRaw(() => kb.ReadLayer(layer).ToArray()), r => new KeyLayer(r).ToArray(), r => new KeyLayer(r).IsValid);
        var macros = kb.ReadMacros();
        Console.WriteLine($"macros: {macros.Count} parsed");
        foreach (var m in macros)
            Console.WriteLine($"  {m.Name}: " + string.Join(", ", m.Events.Select(e => $"{(e.Down ? "↓" : "↑")}{HidUsage.Name(e.Usage)} {e.DelayMs}ms")));
        Console.WriteLine(bad == 0 ? "verify: OK" : $"verify: {bad} problem(s)");
        return bad == 0 ? 0 : 3;
    }
    case "dump":
    {
        var p = kb.ReadProfile();
        Console.WriteLine($"effect 0x{p.Effect:X2} {Effects.Get(p.Effect).Name}");
        foreach (var e in Effects.All.Where(e => e.Id != Effects.Off))
            Console.WriteLine($"  {e.Name,-24} brightness {p.GetBrightness(e.Id)} speed {p.GetSpeed(e.Id)} slot {p.GetColourSlot(e.Id)}");
        foreach (Layer layer in Enum.GetValues<Layer>())
        {
            var keys = kb.ReadLayer(layer);
            var defaults = Defaults.Layer(layer);
            Console.WriteLine($"layer {layer}:");
            foreach (var key in KeyLayout.Keys)
            {
                var a = keys[key.Led];
                if (a == KeyAction.Disabled && layer != Layer.Default)
                    continue;
                string mark = a == defaults[key.Led] ? " " : "*";
                Console.WriteLine($" {mark} {key.Label,-10} -> {a.Describe()}");
            }
        }
        return 0;
    }
    case "backup" when args.Length > 1:
    {
        Directory.CreateDirectory(args[1]);
        File.WriteAllBytes(Path.Combine(args[1], "profile.bin"), kb.ReadProfile().ToArray());
        File.WriteAllBytes(Path.Combine(args[1], "palette.bin"), kb.ReadPalette().ToArray());
        File.WriteAllBytes(Path.Combine(args[1], "perkey.bin"), kb.ReadPerKeyColors().ToArray());
        foreach (Layer layer in Enum.GetValues<Layer>())
            File.WriteAllBytes(Path.Combine(args[1], $"layer_{layer}.bin"), kb.ReadLayer(layer).ToArray());
        File.WriteAllBytes(Path.Combine(args[1], "macros.bin"), Macro.BuildBlob(kb.ReadMacros()));
        Console.WriteLine($"saved to {Path.GetFullPath(args[1])}");
        return 0;
    }
    case "restore" when args.Length > 1:
    {
        var profile = new LightProfile(File.ReadAllBytes(Path.Combine(args[1], "profile.bin")));
        if (!profile.IsValid)
            throw new InvalidDataException("profile.bin has no 5A A5 trailer");
        foreach (Layer layer in Enum.GetValues<Layer>())
        {
            var keys = new KeyLayer(File.ReadAllBytes(Path.Combine(args[1], $"layer_{layer}.bin")));
            if (!keys.IsValid)
                throw new InvalidDataException($"layer_{layer}.bin has no 5A A5 trailer");
            kb.WriteLayer(layer, keys);
        }
        kb.WriteProfile(profile);
        kb.WritePalette(new Palette(File.ReadAllBytes(Path.Combine(args[1], "palette.bin"))));
        kb.WritePerKeyColors(new PerKeyColors(File.ReadAllBytes(Path.Combine(args[1], "perkey.bin"))));
        var macros = Macro.ParseBlob(File.ReadAllBytes(Path.Combine(args[1], "macros.bin")));
        if (macros.Count > 0)
            kb.WriteMacros(macros);
        Console.WriteLine("restored");
        return 0;
    }
    case "effect" when args.Length > 1:
    {
        byte id = Convert.ToByte(args[1], args[1].StartsWith("0x") ? 16 : 10);
        var p = kb.ReadProfile();
        p.Effect = id;
        if (args.Length > 2)
            p.SetBrightness(id, byte.Parse(args[2]));
        if (args.Length > 3)
            p.SetSpeed(id, byte.Parse(args[3]));
        kb.WriteProfile(p);
        Console.WriteLine($"effect -> {Effects.Get(id).Name}");
        return 0;
    }
    case "macro-add" when args.Length > 2:
    {
        // macro-add <name> <key> [key...]: appends a macro that taps the given keys (A-Z, 0-9).
        var macros = kb.ReadMacros().ToList();
        var macro = new Macro { Name = args[1] };
        foreach (string k in args.Skip(2))
        {
            byte usage = HidUsage.All.First(u => string.Equals(u.Value, k, StringComparison.OrdinalIgnoreCase)).Key;
            macro.Events.Add(new MacroEvent(true, usage, 30));
            macro.Events.Add(new MacroEvent(false, usage, 30));
        }
        macros.Add(macro);
        kb.WriteMacros(macros);
        var back = kb.ReadMacros();
        Console.WriteLine($"wrote {macros.Count} macro(s), read back {back.Count}:");
        foreach (var m in back)
            Console.WriteLine($"  {m.Name}: {m.Events.Count} events");
        return back.Count == macros.Count ? 0 : 4;
    }
    case "light" when args.Length > 2:
    {
        // light <seconds> <led>=<RRGGBB> ...: streams a direct frame (nothing is stored).
        int seconds = int.Parse(args[1]);
        var frame = new byte[AulaKeyboard.LedCount * 3];
        foreach (string spec in args.Skip(2))
        {
            var parts = spec.Split('=');
            int led = int.Parse(parts[0]);
            int rgb = Convert.ToInt32(parts[1], 16);
            frame[led * 3] = (byte)(rgb >> 16);
            frame[led * 3 + 1] = (byte)(rgb >> 8);
            frame[led * 3 + 2] = (byte)rgb;
        }
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            kb.SendDirectFrame(frame);
            Thread.Sleep(50);
        }
        return 0;
    }
    case "probe-unused":
    {
        // Gives every unused default-layer entry a distinct printable key, so pressing an
        // unknown physical key reveals its matrix index. "probe-unused off" restores defaults.
        var layer = Defaults.Layer(Layer.Default);
        if (args.Length > 1 && args[1] == "off")
        {
            kb.WriteLayer(Layer.Default, layer);
            Console.WriteLine("default layer restored");
            return 0;
        }
        var used = KeyLayout.Keys.Select(k => k.Led).ToHashSet();
        byte[] codes = [.. Enumerable.Range(0x04, 26).Select(i => (byte)i), .. Enumerable.Range(0x1E, 10).Select(i => (byte)i), 0x2D, 0x2E, 0x2F, 0x30, 0x33];
        int n = 0;
        for (int index = 0; index < KeyLayer.Entries && n < codes.Length; index++)
        {
            if (used.Contains(index) || layer[index] != KeyAction.Disabled)
                continue;
            layer[index] = KeyAction.Key(codes[n]);
            Console.WriteLine($"index {index,3} -> {HidUsage.Name(codes[n])}");
            n++;
        }
        kb.WriteLayer(Layer.Default, layer);
        return 0;
    }
    case "macro-clear":
        kb.WriteMacros([]);
        Console.WriteLine($"macros now: {kb.ReadMacros().Count}");
        return 0;
    case "factory-reset":
        kb.RestoreFactoryDefaults();
        Console.WriteLine("factory tables written");
        return 0;
    default:
        Console.Error.WriteLine($"unknown command {args[0]}");
        return 1;
}

static byte[] ReadRaw(Func<byte[]> read) => read();

static int Check(string name, byte[] raw, Func<byte[], byte[]> roundTrip, Func<byte[], bool> valid)
{
    bool same = roundTrip(raw).AsSpan().SequenceEqual(raw);
    bool ok = valid(raw);
    Console.WriteLine($"{name,-14} {raw.Length,4} bytes  round-trip {(same ? "ok" : "MISMATCH")}  trailer {(ok ? "ok" : "missing")}");
    return same && ok ? 0 : 1;
}
