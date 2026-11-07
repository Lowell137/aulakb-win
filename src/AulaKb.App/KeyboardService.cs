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

    /// <summary>Sends a fresh direct frame every 50 ms until stopped. Nothing is saved on the keyboard.</summary>
    public static void StartStream(Func<byte[]> nextFrame)
    {
        var keyboard = Keyboard ?? Connect() ?? throw new InvalidOperationException(Loc.S("error.notfound_short"));
        StopStream();
        var cts = new CancellationTokenSource();
        _stream = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    keyboard.SendDirectFrame(nextFrame());
                    await Task.Delay(50, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(_stream, cts))
                    _stream = null;
                StreamFailed?.Invoke(ex);
            }
        });
    }

    public static void StopStream()
    {
        _stream?.Cancel();
        _stream = null;
    }

    public static void Shutdown() => Disconnect();
}
