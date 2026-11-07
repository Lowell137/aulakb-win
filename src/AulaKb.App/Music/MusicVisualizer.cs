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
    double _hue;
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
            _window[_filled++] = mono / channels;
            if (_filled == FftSize)
            {
                Analyse();
                _filled = 0;
            }
        }
    }

    void Analyse()
    {
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
            _beats += beats;
        }
    }

    /// <summary>Builds one 378-byte direct frame from the current levels.</summary>
    public byte[] BuildFrame()
    {
        float[] levels;
        int beats;
        lock (_lock)
        {
            levels = (float[])_levels.Clone();
            beats = _beats;
            _beats = 0;
        }
        double t = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(t - _last, 0, 0.2);
        _last = t;
        float overall = Math.Clamp(levels.Average() * 1.25f, 0, 1);
        _overallSmooth += (overall - _overallSmooth) * Math.Min(1, dt * 12);
        _hue = (_hue + dt * 0.05) % 1.0;
        if (beats > 0)
            _hue = (_hue + 0.13) % 1.0;

        var frame = new byte[Leds * 3];
        switch (Style)
        {
            case MusicStyle.AudioDance: Flower(frame); break;
            case MusicStyle.Dazzling: Rows(frame, levels, t); break;
            case MusicStyle.CloudsAndSnow: WholeBoard(frame); break;
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

    void Flower(byte[] frame)
    {
        // Captured: a hexagonal flower centred on the letter block, each ring its own colour.
        float cx = 7.3f, cy = 2.6f;
        float rings = 0.3f + _overallSmooth * 3.6f;
        for (int led = 0; led < Leds; led++)
        {
            if (!_has[led])
                continue;
            float dx = Math.Abs(_ux[led] - cx), dy = Math.Abs(_row[led] - cy);
            float d = Math.Max(dy, dx * 0.6f + dy * 0.5f);   // hexagon
            float k = Math.Clamp(rings - d + 0.5f, 0, 1);
            Put(frame, led, k, _hue + (int)d * 0.12);
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

    void WholeBoard(byte[] frame)
    {
        float k = Math.Clamp((_overallSmooth - 0.05f) * 1.4f, 0, 1); // dark when quiet, as captured
        for (int led = 0; led < Leds; led++)
            if (_has[led])
                Put(frame, led, k, _hue);
    }

    void Pearls(byte[] frame, int beats, double t, bool small = false)
    {
        // Gurgling stream also opens a flower whenever the music is loud, not only on beats.
        int spawn = beats + (small && _random.NextDouble() < _overallSmooth * 0.12 ? 1 : 0);
        for (int i = 0; i < spawn; i++)
        {
            int led;
            do led = _random.Next(Leds); while (!_has[led]);
            _blooms.Add((_ux[led], _row[led], (float)_random.NextDouble(), t));
        }
        double life = small ? 0.8 : 0.6;
        _blooms.RemoveAll(b => t - b.Born > life);
        foreach (var b in _blooms)
        {
            float age = (float)((t - b.Born) / life);          // 0..1
            float radius = small ? 1f + age * 2.4f : 0.8f + age * 4.2f;
            float fade = 1 - age * age;
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
            _strokes.Add((_random.Next(6), (float)_random.NextDouble() * (_width - length), length, (float)_random.NextDouble(), t));
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
                r.Hue = (float)_random.NextDouble();
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
