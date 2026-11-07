namespace AulaKb.Core.Hid;

/// <summary>A HID top-level collection that we talk to with feature reports.</summary>
public interface IFeatureDevice : IDisposable
{
    string Path { get; }

    /// <summary>Sends a feature report. <paramref name="report"/>[0] is the report id.</summary>
    bool SetFeature(byte[] report);

    /// <summary>Reads a feature report into <paramref name="report"/>; set [0] to the report id first.</summary>
    bool GetFeature(byte[] report);
}

public sealed record HidDeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    int InputLength,
    int OutputLength,
    int FeatureLength,
    ushort VersionNumber = 0);
