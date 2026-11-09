using System.Diagnostics;
using AulaKb.App.Controls;
using AulaKb.Core;
using NAudio.Dsp;
using NAudio.Wave;

namespace AulaKb.App.Music;

/// <summary>
/// The vendor tool's music modes for the F87 Pro wired (ten: "Paoma" exists in its text table
/// but is not offered for this model). Each look was rebuilt from a labelled capture of the
/// vendor tool's direct frames (2026-11-08, each mode selected in turn and marked in the log;
/// the vendor tool applies a selection ~12.5 s late, which the split accounts for).
/// Full-saturation colours, keys lit or off, only the ends of shapes at partial brightness.
/// </summary>
public enum MusicStyle
{
    AudioDance,      // a flower of coloured rings in the middle, size = loudness
    Dazzling,        // every row a bar from the left edge (bass at the bottom), sliding rainbow
    CloudsAndSnow,   // whole board one colour, brightness follows the music, colour steps on beats
    LightField,      // full-width bars from the bottom, rainbow across the board
    GurglingStream,  // small flowers opening at random keys on beats
    Blooming,        // bars from the bottom, a colour per bar
    PearlsOnJade,    // short rainbow row segments flickering in
    CloudsAndMoon,   // quiet: a small mound rising from the bottom middle on loud parts
    Mountains,       // a zigzag line across the board, moving with the music
    SilkRain,        // slanted streaks running across
}

/// <summary>
/// Software music mode, the same mechanism as the vendor tool: system audio (WASAPI loopback)
/// is analysed on the PC and streamed as 0x08 direct frames. Nothing is stored on the keyboard.
/// </summary>
public sealed class MusicVisualizer : IDisposable
{
    const int FftSize = 1024;
    const int Bands = 17;
    const int Leds = AulaKeyboard.LedCount;

    enum Profile { Soft, Rock, Routine, Voice, Regular, Passion }

    static Profile ProfileOf(MusicStyle s) => s switch
    {
        MusicStyle.AudioDance => Profile.Soft,
        MusicStyle.Dazzling or MusicStyle.PearlsOnJade => Profile.Rock,
        MusicStyle.CloudsAndSnow => Profile.Routine,
        MusicStyle.LightField => Profile.Voice,
        MusicStyle.Blooming or MusicStyle.CloudsAndMoon => Profile.Passion,
        _ => Profile.Regular,
    };

    readonly object _lock = new();
    readonly float[] _window = new float[FftSize];
    readonly float[] _levels = new float[Bands];
    readonly Random _random = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    WasapiLoopbackCapture? _capture;
    int _filled;
    int _sampleRate = 48000;
    float _bassAverage;
    long _lastBeatMs;
    int _beats;

    // Key geometry by LED: centre in key units (1u = one key), row 0 (F row) .. 5 (Ctrl row).
    readonly float[] _ux = new float[Leds];
    readonly int[] _row = new int[Leds];
    readonly bool[] _has = new bool[Leds];
    readonly float _width;
    readonly int[] _index = new int[Leds];
    readonly int[] _rowCount = new int[6];

    // state of the animated modes
    double _last;
    float _overallSmooth;
    double _hue;          // slow drift, never jumps: the vendor's colours move at most ~10° a frame
    int _boardColour;     // Clouds and snow: palette colour, steps on beats
    readonly List<(float X, int Row, float Hue, double Born)> _blooms = [];
    readonly List<(int Row, float X0, float Length, float Hue, double Born)> _strokes = [];
    readonly (float X, float Hue)[] _runners = new (float, float)[5];

    public MusicVisualizer()
    {
        var keys = VisualLayout.Build(VisualLayout.Current).Select(v => v.Key).Where(k => k.Led < Leds).ToList();
        double unit = 32; // layout.json pitch
        double minX = keys.Min(k => k.X), minY = keys.Min(k => k.Y);
        foreach (var k in keys)
        {
            _has[k.Led] = true;
            _ux[k.Led] = (float)((k.X + k.W / 2.0 - minX) / unit);
            _row[k.Led] = Math.Clamp((int)Math.Round((k.Y - minY) / unit - (k.Y > minY ? 0.25 : 0)), 0, 5);
        }
        _width = (float)((keys.Max(k => k.X + k.W) - minX) / unit);
        // position of each key within its row, counted in keys (Dazzling measures rows this way)
        foreach (var group in keys.GroupBy(k => _row[k.Led]))
        {
            var ordered = group.OrderBy(k => k.X).ToList();
            _rowCount[group.Key] = ordered.Count;
            for (int i = 0; i < ordered.Count; i++)
                _index[ordered[i].Led] = i;
        }
        for (int i = 0; i < _runners.Length; i++)
            _runners[i] = (i * _width / _runners.Length, i / (float)_runners.Length);
    }

    public MusicStyle Style { get; set; } = MusicStyle.AudioDance;

    /// <summary>Input gain, 0.5 .. 4.</summary>
    public float Gain { get; set; } = 1.5f;

    /// <summary>0 = instant fall, 9 = very slow fall.</summary>
    public int Smoothness { get; set; } = 4;

    public bool Colourful { get; set; } = true;

    public Rgb Colour { get; set; } = new(0, 255, 0);

    public void Start()
    {
        Stop();
        _capture = new WasapiLoopbackCapture();
        _sampleRate = _capture.WaveFormat.SampleRate;
        _capture.DataAvailable += OnData;
        _capture.StartRecording();
    }

    public void Stop()
    {
        if (_capture is null)
            return;
        _capture.DataAvailable -= OnData;
        try
        {
            _capture.StopRecording();
        }
        catch (Exception)
        {
        }
        _capture.Dispose();
        _capture = null;
        lock (_lock)
            Array.Clear(_levels);
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        var format = _capture?.WaveFormat;
        if (format is null || format.Encoding != WaveFormatEncoding.IeeeFloat && format.BitsPerSample != 32)
            return;
        int channels = format.Channels;
        var samples = new WaveBuffer(e.Buffer).FloatBuffer;
        int count = e.BytesRecorded / 4;
        for (int i = 0; i + channels <= count; i += channels)
        {
            float mono = 0;
            for (int c = 0; c < channels; c++)
                mono += samples[i + c];
            _left[_filled] = samples[i];
            _window[_filled++] = mono / channels;
            if (_filled == FftSize)
            {
                Analyse();
                _filled = 0;
            }
        }
    }

    // ---- the vendor tool's analysis (decompiled from OemDrv.exe, FUN_00480860 / FUN_00454120) ----
    // 1024 samples of the left channel as 16-bit values, no window, one FFT per block. Each of 23
    // bands is ONE bin at 1024·f/fs: value = (int)(20·log10((re²+im²) / 32767²)), negatives → 0,
    // times the gain (0.5 + 0.1·step, default 1.0), clamped 0..100, then the average of the last
    // N blocks (N = smoothness, default 4). Every music mode reads these 23 values.
    static readonly int[] VendorBandHz = [30, 60, 100, 160, 240, 300, 350, 400, 440, 500, 600, 800, 1000, 1500, 2000, 2600, 3000, 4000, 6000, 8000, 10000, 14000, 16000];
    const int VendorBands = 23;
    readonly float[] _left = new float[FftSize];
    readonly int[] _vendorBands = new int[VendorBands];
    readonly List<int[]> _vendorHistory = [];

    void AnalyseVendor()
    {
        var data = new Complex[FftSize];
        for (int i = 0; i < FftSize; i++)
        {
            data[i].X = (float)(int)(Math.Clamp(_left[i], -1f, 1f) * 32767);
            data[i].Y = 0;
        }
        FastFourierTransform.FFT(true, 10, data); // NAudio scales the forward transform by 1/N
        float gain = Math.Clamp(Gain / 1.5f, 0.5f, 2.0f); // our slider's default 1.5 = the vendor's 1.0
        var block = new int[VendorBands];
        for (int b = 0; b < VendorBands; b++)
        {
            if (VendorBandHz[b] > _sampleRate / 2)
                continue;
            int bin = (int)((long)FftSize * VendorBandHz[b] / _sampleRate);
            double re = data[bin].X * (double)FftSize, im = data[bin].Y * (double)FftSize;
            double power = re * re + im * im;
            int db = power > 0 ? (int)(20 * Math.Log10(power / (32767.0 * 32767.0))) : 0;
            if (db < 0)
                db = 0;
            block[b] = Math.Min(100, (int)(db * gain));
        }
        int n = Math.Clamp(Smoothness, 1, 9);
        lock (_lock)
        {
            _vendorHistory.Add(block);
            while (_vendorHistory.Count > n)
                _vendorHistory.RemoveAt(0);
            for (int b = 0; b < VendorBands; b++)
            {
                int sum = 0;
                foreach (var h in _vendorHistory)
                    sum += h[b];
                _vendorBands[b] = sum / _vendorHistory.Count;
            }
        }
    }

    void Analyse()
    {
        AnalyseVendor();
        var data = new Complex[FftSize];
        for (int i = 0; i < FftSize; i++)
        {
            data[i].X = (float)(_window[i] * FastFourierTransform.HannWindow(i, FftSize));
            data[i].Y = 0;
        }
        FastFourierTransform.FFT(true, 10, data);

        var profile = ProfileOf(Style);
        float gain = Gain * profile switch { Profile.Soft => 0.85f, Profile.Passion => 1.35f, Profile.Rock => 1.15f, _ => 1f };
        double binHz = (double)_sampleRate / FftSize;
        double lo = 40, hi = 16000;
        var bands = new float[Bands];
        for (int b = 0; b < Bands; b++)
        {
            double f0 = lo * Math.Pow(hi / lo, (double)b / Bands);
            double f1 = lo * Math.Pow(hi / lo, (double)(b + 1) / Bands);
            int i0 = Math.Max(1, (int)(f0 / binHz));
            int i1 = Math.Max(i0 + 1, (int)(f1 / binHz));
            double sum = 0;
            for (int i = i0; i < i1 && i < FftSize / 2; i++)
                sum += Math.Sqrt(data[i].X * data[i].X + data[i].Y * data[i].Y);
            double magnitude = sum / (i1 - i0);
            double weight = profile switch
            {
                Profile.Voice => b is >= 6 and <= 11 ? 1.4 : 0.6,
                Profile.Rock => b <= 3 ? 1.3 : 1.0,
                _ => 1.0,
            };
            // high bands carry less energy; tilt them up so the right side of bars moves too
            weight *= 1 + b * 0.06;
            bands[b] = (float)Math.Clamp(Math.Log10(1 + 900 * magnitude * gain * weight) / 2.2, 0, 1);
        }

        float fall = 0.04f + 0.1f * (9 - Math.Clamp(Smoothness, 0, 9)) / 9f;
        fall *= profile switch { Profile.Soft => 0.6f, Profile.Rock or Profile.Passion => 1.5f, _ => 1f };

        float bass = (bands[0] + bands[1] + bands[2] + bands[3]) / 4;
        float threshold = profile is Profile.Rock or Profile.Passion ? 1.25f : 1.4f;
        long now = _clock.ElapsedMilliseconds;
        lock (_lock)
        {
            for (int b = 0; b < Bands; b++)
                _levels[b] = Math.Max(bands[b], _levels[b] - fall);
            if (bass > 0.2f && bass > _bassAverage * threshold && now - _lastBeatMs > 160)
            {
                _beats++;
                _lastBeatMs = now;
            }
            _bassAverage = _bassAverage * 0.93f + bass * 0.07f;
        }
    }

    /// <summary>Selftest only: sets band levels and pending beats as if audio had been analysed.</summary>
    internal void Inject(float[] levels, int beats)
    {
        lock (_lock)
        {
            Array.Copy(levels, _levels, Math.Min(levels.Length, Bands));
            for (int b = 0; b < VendorBands; b++)
                _vendorBands[b] = (int)(Math.Clamp(levels[Math.Min(levels.Length - 1, b * levels.Length / VendorBands)], 0, 1) * 100);
            _beats += beats;
        }
    }

    /// <summary>Builds one 378-byte direct frame from the current levels.</summary>
    public byte[] BuildFrame()
    {
        float[] levels;
        int beats;
        int[] vb;
        lock (_lock)
        {
            vb = (int[])_vendorBands.Clone();
            levels = (float[])_levels.Clone();
            beats = _beats;
            _beats = 0;
        }
        double t = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(t - _last, 0, 0.2);
        _last = t;
        float overall = Math.Clamp(levels.Average() * 1.25f, 0, 1);
        // Captured: the vendor's shapes jump up at once on a loud moment and then fall back at a
        // constant rate (a peak hold with a linear fall), they don't ease in and out.
        _overallSmooth = Math.Max(overall, _overallSmooth - dt * 1.1f);
        _flowerSize = Math.Max(Math.Min(7.3f, overall * 7.5f), _flowerSize - dt * FlowerFallPerSecond);
        _hue = (_hue + dt * 0.05) % 1.0;
        if (beats > 0)
            _boardColour = (_boardColour + 1 + _random.Next(PaletteHues.Length - 1)) % PaletteHues.Length;

        var frame = new byte[Leds * 3];
        switch (Style)
        {
            case MusicStyle.AudioDance: Flower(frame, vb, t); break;
            case MusicStyle.Dazzling: Rows(frame, levels, t); break;
            case MusicStyle.CloudsAndSnow: WholeBoard(frame, vb); break;
            case MusicStyle.LightField: Bars(frame, levels, perBarHue: false, t); break;
            case MusicStyle.GurglingStream: Pearls(frame, beats, t, small: true); break;
            case MusicStyle.Blooming: Bars(frame, levels, perBarHue: true, t); break;
            case MusicStyle.PearlsOnJade: Silk(frame, overall, t); break;
            case MusicStyle.CloudsAndMoon: Mound(frame); break;
            case MusicStyle.Mountains: Zigzag(frame, overall, dt); break;
            case MusicStyle.SilkRain: Runners(frame, overall, dt); break;
        }
        return frame;
    }

    // Audio dance rings as the vendor tool lights them (LED numbers, centre = Y key column).
    static readonly int[][] FlowerRings =
    [
        [44],
        [38, 39, 43, 45, 49, 50],
        [32, 33, 34, 37, 40, 42, 46, 51, 55, 56],
        [26, 27, 28, 31, 35, 36, 48, 52, 57, 61, 62],
        [20, 21, 22, 25, 30, 54, 58, 63, 67, 68],
        [14, 15, 16, 19, 24, 53, 60, 64, 69, 73, 74],
        [8, 9, 10, 13, 17, 18, 59, 65, 66, 72, 79, 80, 81, 82, 83],
    ];

    // Audio dance colour clock, captured: green → blue → cyan → red → magenta → yellow, round
    // and round regardless of the music. The colour changes every 2 frames (~94 ms): it holds a
    // colour 4 steps, then fades to the next one in 8 even RGB steps (1.125 s per colour). Each
    // ring outwards shows the centre's colour from 0.47 s earlier, so colours flow outwards.
    static readonly Rgb[] FlowerCycle =
    [
        new(0, 255, 0), new(0, 0, 255), new(0, 255, 255), new(255, 0, 0), new(255, 0, 255), new(255, 255, 0),
    ];
    const double ClockStep = 0.0935;
    const double RingDelay = 0.47;

    static Rgb FlowerColour(double t)
    {
        long step = (long)Math.Floor(t / ClockStep);
        int n = FlowerCycle.Length;
        int index = (int)(((step / 12) % n + n) % n);
        int within = (int)(((step % 12) + 12) % 12);
        var a = FlowerCycle[index];
        var b = FlowerCycle[(index + 1) % n];
        float f = Math.Clamp((within - 4) / 8f, 0, 1);
        return new Rgb((byte)(a.R + (b.R - a.R) * f), (byte)(a.G + (b.G - a.G) * f), (byte)(a.B + (b.B - a.B) * f));
    }

    float _flowerSize;
    const float FlowerFallPerSecond = 0.36f / 0.0468f; // captured: 0.36 ring per frame
    int _flowerLevel;

    void Flower(byte[] frame, int[] vb, double t)
    {
        // Vendor (FUN_0047cb40, mode 1): the level is band 1 (60 Hz). It jumps up at once and
        // falls at most 5 a frame. Rings lit = level / (100/7) from the centre (U, flower.txt);
        // the remainder lights one more ring at remainder/14 brightness.
        int level = Math.Min(100, vb[1]);
        _flowerLevel = level < _flowerLevel ? Math.Max(0, _flowerLevel - Math.Min(_flowerLevel - level, 5)) : level;
        int rings = FlowerRings.Length;
        int per = 100 / rings; // 14
        int lit = (int)(_flowerLevel / (100.0 / rings));
        int rem = (int)(_flowerLevel - lit * (100.0 / rings));
        if (rem > 0 && lit < rings)
            lit++;
        for (int ring = 0; ring < rings; ring++)
        {
            float k = ring >= lit ? 0 : ring == lit - 1 && rem > 0 ? Math.Min(1f, rem / (float)per) : 1f;
            var colour = FlowerColour(t - ring * RingDelay);
            foreach (int led in FlowerRings[ring])
                PutRgb(frame, led, k, colour);
        }
    }

    void Mound(byte[] frame)
    {
        // Captured: mostly dark; on loud parts a small block grows up from the bottom middle.
        float level = Math.Clamp((_overallSmooth - 0.58f) / 0.42f, 0, 1);
        float height = level * 4;
        for (int led = 0; led < Leds; led++)
        {
            if (!_has[led])
                continue;
            float fromBottom = 5 - _row[led];
            float half = (height - fromBottom) * 1.6f;
            float k = Math.Clamp(Math.Min(half - Math.Abs(_ux[led] - 7.5f) + 0.5f, height - fromBottom), 0, 1);
            Put(frame, led, k, _hue + _ux[led] * 0.04);
        }
    }

    double _zig;

    void Zigzag(byte[] frame, float overall, float dt)
    {
        // Captured: a W-shaped line from the bottom row to the top row and back, drifting sideways.
        _zig += dt * (1.5 + overall * 6);
        const float period = 7f;   // keys per V
        for (int led = 0; led < Leds; led++)
        {
            if (!_has[led])
                continue;
            float phase = (float)(((_ux[led] + _zig) / period) % 1.0);
            float tri = Math.Abs(phase * 2 - 1);          // 1 at the edges of a V, 0 in its middle
            float rowOnLine = tri * 5;                      // 0 top .. 5 bottom
            float k = Math.Clamp(1.2f - Math.Abs(_row[led] - rowOnLine) * 1.7f, 0, 1); // one key per column
            int segment = (int)Math.Floor((_ux[led] + _zig) / (period / 2));
            Put(frame, led, k, segment * 0.17 + _hue);
        }
    }

    // ---- shared looks ----

    void Bars(byte[] frame, float[] levels, bool perBarHue, double t)
    {
        // One bar about every 1.1 keys, lit from the bottom row up; the top segment is partial.
        // Light field's bars reach higher than Blooming's (captured row coverage).
        float scale = perBarHue ? 5.6f : 7.2f;
        for (int led = 0; led < Leds; led++)
        {
            if (!_has[led])
                continue;
            float pos = _ux[led] / _width;
            int band = Math.Clamp((int)(pos * Bands), 0, Bands - 1);
            float height = Math.Max(0, levels[band] - 0.1f) / 0.9f * scale; // the bottom key stays dark when quiet
            float fromBottom = 5 - _row[led];
            float k = Math.Clamp(height - fromBottom, 0, 1);
            double hue = perBarHue ? band * 0.37 + t * 0.02 : pos * 0.85 + t * 0.03;
            Put(frame, led, k, hue);
        }
    }

    readonly float[] _rowLength = new float[6];

    void Rows(byte[] frame, float[] levels, double t)
    {
        // Captured (Dazzling): every row is a bar from the left edge, the bottom row follows the
        // bass and the top row the treble; it jumps out at once and pulls back quickly. The colour
        // is a rainbow along the row, about 23° of hue per key, sliding right ~36°/s.
        for (int row = 0; row < 6; row++)
        {
            int b0 = (5 - row) * Bands / 6, b1 = Math.Max(b0 + 1, (6 - row) * Bands / 6);
            float level = 0;
            for (int b = b0; b < b1; b++)
                level = Math.Max(level, levels[b]);
            // length in keys of that row (the bottom row has few keys, so Space is reached early)
            if (row == 5)
                level = Math.Min(1, level * 1.3f); // the bass row runs longest in the capture
            float target = level * (_rowCount[row] + 1);
            _rowLength[row] = target > _rowLength[row] ? target : Math.Max(target, _rowLength[row] - _rowCount[row] * 0.06f);
        }
        double slide = t * 36 / 360.0;
        for (int led = 0; led < Leds; led++)
        {
            if (!_has[led])
                continue;
            float k = Math.Clamp(_rowLength[_row[led]] - _index[led], 0, 1);
            // starts at yellow on the left edge, goes through green, blue, violet to red
            Put(frame, led, k, 60 / 360.0 + _index[led] * 23 / 360.0 - slide);
        }
    }

    int _cloudLevel;
    Rgb _cloudColour = new(0, 0, 255);

    void WholeBoard(byte[] frame, int[] vb)
    {
        // Vendor (mode 3): band 2 (100 Hz), or band 12 (1 kHz) when that is silent. Rising: the
        // colour is picked by how loud it is (blue < 41, cyan < 51, green < 61, yellow < 71,
        // orange < 81, magenta < 91, red); falling: at most 10 a frame. Brightness = level %.
        int level = vb[2] < 1 ? vb[12] : vb[2];
        level = Math.Min(100, level);
        if (level < 1 || level < _cloudLevel)
            _cloudLevel = Math.Max(0, _cloudLevel - Math.Min(_cloudLevel - level, 10));
        else
        {
            _cloudColour = level switch
            {
                < 41 => new Rgb(0, 0, 255),
                < 51 => new Rgb(0, 255, 255),
                < 61 => new Rgb(0, 255, 0),
                < 71 => new Rgb(255, 255, 0),
                < 81 => new Rgb(255, 128, 0),
                < 91 => new Rgb(255, 0, 255),
                _ => new Rgb(255, 0, 0),
            };
            _cloudLevel = level;
        }
        for (int led = 0; led < Leds; led++)
            if (_has[led])
                PutRgb(frame, led, _cloudLevel / 100f, _cloudColour);
    }

    // The vendor's colours are pure: red, orange, yellow, green, cyan, blue, magenta.
    static readonly double[] PaletteHues = [0, 30 / 360.0, 60 / 360.0, 120 / 360.0, 180 / 360.0, 240 / 360.0, 300 / 360.0];

    double RandomPaletteHue() => PaletteHues[_random.Next(PaletteHues.Length)];

    void Pearls(byte[] frame, int beats, double t, bool small = false)
    {
        // Gurgling stream also opens a flower whenever the music is loud, not only on beats.
        int spawn = beats + (small && _random.NextDouble() < _overallSmooth * 0.12 ? 1 : 0);
        for (int i = 0; i < spawn; i++)
        {
            int led;
            do led = _random.Next(Leds); while (!_has[led]);
            _blooms.Add((_ux[led], _row[led], (float)RandomPaletteHue(), t));
        }
        // Captured (Gurgling stream): a key brightens in ~3 frames (20 % → 40 % → full), stays a
        // few frames and goes dark at once, no slow fade.
        double life = small ? 0.25 : 0.6;
        _blooms.RemoveAll(b => t - b.Born > life);
        foreach (var b in _blooms)
        {
            float age = (float)((t - b.Born) / life);          // 0..1
            float radius = small ? 1f + age * 1.2f : 0.8f + age * 4.2f;
            float fade = small ? Math.Clamp((float)(t - b.Born) / 0.12f, 0.2f, 1f) : 1 - age * age;
            for (int led = 0; led < Leds; led++)
            {
                if (!_has[led])
                    continue;
                float d = Math.Max(Math.Abs(_ux[led] - b.X) * 0.8f, Math.Abs(_row[led] - b.Row));
                float k = Math.Clamp(radius - d, 0, 1) * fade;
                PutMax(frame, led, k, b.Hue);
            }
        }
    }

    void Silk(byte[] frame, float overall, double t)
    {
        if (_random.NextDouble() < overall * 1.6)
        {
            float length = 2 + overall * _width * 0.6f;
            // colours near the drifting hue, so the board does not flicker between random colours
            _strokes.Add((_random.Next(6), (float)_random.NextDouble() * (_width - length), length, (float)(_hue + _random.NextDouble() * 0.12), t));
        }
        _strokes.RemoveAll(s => t - s.Born > 0.14);
        foreach (var s in _strokes)
            for (int led = 0; led < Leds; led++)
                if (_has[led] && _row[led] == s.Row && _ux[led] >= s.X0 && _ux[led] <= s.X0 + s.Length)
                    PutMax(frame, led, 1, s.Hue + (_ux[led] - s.X0) / _width * 0.8);
    }

    void Runners(byte[] frame, float overall, float dt)
    {
        float speed = 4 + overall * 22; // keys per second
        for (int i = 0; i < _runners.Length; i++)
        {
            var r = _runners[i];
            r.X += speed * dt * (0.8f + i * 0.1f);
            if (r.X > _width + 6)
            {
                r.X -= _width + 9;
                r.Hue = (float)((_hue + i * 0.17) % 1.0);
            }
            _runners[i] = r;
            for (int led = 0; led < Leds; led++)
            {
                if (!_has[led])
                    continue;
                // a stroke leaning back: lower rows trail behind the top
                float x = r.X - _row[led] * 0.55f;
                float k = Math.Clamp(1 - Math.Abs(_ux[led] - x) * 1.4f, 0, 1);
                PutMax(frame, led, k, r.Hue);
            }
        }
    }

    // ---- output ----

    void Put(byte[] frame, int led, float intensity, double hue)
    {
        intensity = Math.Clamp(intensity, 0, 1);
        if (intensity < 0.05f)
        {
            frame[led * 3] = frame[led * 3 + 1] = frame[led * 3 + 2] = 0;
            return;
        }
        var colour = Colourful ? Hue(((hue % 1.0) + 1.0) % 1.0) : Colour;
        frame[led * 3] = (byte)(colour.R * intensity);
        frame[led * 3 + 1] = (byte)(colour.G * intensity);
        frame[led * 3 + 2] = (byte)(colour.B * intensity);
    }

    void PutRgb(byte[] frame, int led, float intensity, Rgb rgb)
    {
        intensity = Math.Clamp(intensity, 0, 1);
        var colour = Colourful ? rgb : Colour;
        if (intensity < 0.05f)
            colour = new Rgb(0, 0, 0);
        frame[led * 3] = (byte)(colour.R * intensity);
        frame[led * 3 + 1] = (byte)(colour.G * intensity);
        frame[led * 3 + 2] = (byte)(colour.B * intensity);
    }

    void PutMax(byte[] frame, int led, float intensity, double hue)
    {
        int current = Math.Max(frame[led * 3], Math.Max(frame[led * 3 + 1], frame[led * 3 + 2]));
        if (intensity * 255 > current)
            Put(frame, led, intensity, hue);
    }

    static Rgb Hue(double h)
    {
        double r = Math.Clamp(Math.Abs(h * 6 - 3) - 1, 0, 1);
        double g = Math.Clamp(2 - Math.Abs(h * 6 - 2), 0, 1);
        double b = Math.Clamp(2 - Math.Abs(h * 6 - 4), 0, 1);
        return new Rgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    public void Dispose() => Stop();
}
