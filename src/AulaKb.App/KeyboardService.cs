using AulaKb.Core;

namespace AulaKb.App;

/// <summary>Owns the single keyboard connection and the live (direct-frame) stream.</summary>
static class KeyboardService
{
    static CancellationTokenSource? _stream;
    static readonly SemaphoreSlim Gate = new(1, 1);

    public static AulaKeyboard? Keyboard { get; private set; }

    public static bool IsStreaming => _stream is not null;

    /// <summary>Raised on a background thread when the live stream stops because of an error.</summary>
    public static event Action<Exception>? StreamFailed;

    public static AulaKeyboard? Connect()
    {
        Disconnect();
        Keyboard = AulaKeyboard.Find();
        return Keyboard;
    }

    public static void Disconnect()
    {
        StopStream();
        Keyboard?.Dispose();
        Keyboard = null;
    }

    /// <summary>
    /// Runs keyboard I/O off the UI thread. Stops a running live stream first: writing a
    /// profile while direct frames are flowing locked the keyboard during Linux testing.
    /// </summary>
    public static async Task<T> RunAsync<T>(Func<AulaKeyboard, T> work)
    {
        await Gate.WaitAsync();
        try
        {
            if (IsStreaming)
            {
                StopStream();
                await Task.Delay(1500);
            }
            return await Task.Run(() =>
            {
                var kb = Keyboard ?? Connect() ?? throw new InvalidOperationException(Loc.S("error.notfound"));
                return work(kb);
            });
        }
        catch (IOException)
        {
            Disconnect();
            throw;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static Task RunAsync(Action<AulaKeyboard> work) => RunAsync(kb =>
    {
        work(kb);
        return true;
    });

    /// <summary>Sends a fresh direct frame every 46.8 ms (the vendor tool's rate) until stopped. Nothing is saved on the keyboard.</summary>
    public static void StartStream(Func<byte[]> nextFrame)
    {
        var keyboard = Keyboard ?? Connect() ?? throw new InvalidOperationException(Loc.S("error.notfound_short"));
        StopStream();
        var cts = new CancellationTokenSource();
        _stream = cts;

        // The vendor tool sends a frame every 46.8 ms, evenly. A Delay(50) after each send lands
        // on the 15.6 ms timer grid (~62 ms) plus the send time, so frames came unevenly; this
        // thread keeps fixed deadlines with a 1 ms timer instead.
        var thread = new Thread(() =>
        {
            timeBeginPeriod(1);
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                double next = 0;
                while (!cts.IsCancellationRequested)
                {
                    keyboard.SendDirectFrame(nextFrame());
                    next += FrameMs;
                    double wait = next - clock.Elapsed.TotalMilliseconds;
                    if (wait < -FrameMs)
                        next = clock.Elapsed.TotalMilliseconds; // fell behind (USB hiccup): no burst
                    else if (wait >= 1)
                        cts.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(wait));
                }
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(_stream, cts))
                    _stream = null;
                StreamFailed?.Invoke(ex);
            }
            finally
            {
                timeEndPeriod(1);
            }
        })
        { IsBackground = true, Name = "AulaKb frames", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    const double FrameMs = 46.8;

    [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
    [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

    public static void StopStream()
    {
        _stream?.Cancel();
        _stream = null;
    }

    public static void Shutdown() => Disconnect();
}
