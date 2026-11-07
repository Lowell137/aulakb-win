using AulaKb.Core;
using NAudio.Dsp;
using NAudio.Wave;

namespace AulaKb.App.Music;

public enum MusicStyle
{
    Spectrum,
    Pulse,
    Wave,
}

/// <summary>
/// Software music mode, the same mechanism as the vendor tool: system audio (WASAPI loopback)
/// is analysed on the PC and streamed as 0x08 direct frames. Nothing is stored on the keyboard.
/// LED index = column * 6 + row (row 0 = Esc row, row 5 = Ctrl row).
/// </summary>
public sealed class MusicVisualizer : IDisposable
{
    const int FftSize = 1024;
    const int Columns = 17;
    const int Rows = 6;

    readonly object _lock = new();
    readonly float[] _window = new float[FftSize];
    readonly float[] _levels = new float[Columns];
    WasapiLoopbackCapture? _capture;
    int _filled;
    int _sampleRate = 48000;
    double _phase;

    public MusicStyle Style { get; set; } = MusicStyle.Spectrum;

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

        double binHz = (double)_sampleRate / FftSize;
        double lo = 40, hi = 16000;
        var bands = new float[Columns];
        for (int b = 0; b < Columns; b++)
        {
            double f0 = lo * Math.Pow(hi / lo, (double)b / Columns);
            double f1 = lo * Math.Pow(hi / lo, (double)(b + 1) / Columns);
            int i0 = Math.Max(1, (int)(f0 / binHz));
            int i1 = Math.Max(i0 + 1, (int)(f1 / binHz));
            double sum = 0;
            for (int i = i0; i < i1 && i < FftSize / 2; i++)
                sum += Math.Sqrt(data[i].X * data[i].X + data[i].Y * data[i].Y);
            double magnitude = sum / (i1 - i0);
            // Roughly 0..1 for normal listening levels; louder bands are compressed by the log.
            bands[b] = (float)Math.Clamp(Math.Log10(1 + 900 * magnitude * Gain) / 2.2, 0, 1);
        }

        float fall = 0.04f + 0.1f * (9 - Math.Clamp(Smoothness, 0, 9)) / 9f;
        lock (_lock)
        {
            for (int b = 0; b < Columns; b++)
                _levels[b] = Math.Max(bands[b], _levels[b] - fall);
        }
    }

    /// <summary>Builds one 378-byte direct frame from the current levels.</summary>
    public byte[] BuildFrame()
    {
        float[] levels;
        lock (_lock)
            levels = (float[])_levels.Clone();
        _phase += 0.03;
        var frame = new byte[AulaKeyboard.LedCount * 3];
        float overall = levels.Average();

        for (int col = 0; col < Columns; col++)
        {
            for (int row = 0; row < Rows; row++)
            {
                int led = col * Rows + row;
                if (led >= AulaKeyboard.LedCount)
                    continue;
                float intensity = Style switch
                {
                    MusicStyle.Spectrum => levels[col] * Rows > (Rows - 1 - row) ? 1f : 0f,
                    MusicStyle.Pulse => Math.Clamp(overall * 1.6f, 0, 1),
                    _ => Math.Clamp(levels[col] * (0.6f + 0.4f * (float)Math.Sin(_phase * 4 + col * 0.5 + row)), 0, 1),
                };
                var colour = Colourful ? Hue((col / (double)Columns + (Style == MusicStyle.Wave ? _phase : 0)) % 1.0) : Colour;
                frame[led * 3] = (byte)(colour.R * intensity);
                frame[led * 3 + 1] = (byte)(colour.G * intensity);
                frame[led * 3 + 2] = (byte)(colour.B * intensity);
            }
        }
        return frame;
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
