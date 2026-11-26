namespace AulaKb.Core.Hid;

/// <summary>A HID collection spoken to with input/output reports (the 2.4G receiver).</summary>
public interface IReportDevice : IDisposable
{
    string Path { get; }

    /// <summary>Writes one report; [0] is the report id.</summary>
    bool WriteReport(byte[] report);

    /// <summary>Reads one input report into <paramref name="buffer"/>; returns the byte count, or -1 on timeout/error.</summary>
    int ReadReport(byte[] buffer, int timeoutMs);
}
