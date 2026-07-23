namespace SC3.HidMonitor;

internal sealed record HidDeviceDescriptor(
    string Path,
    string Interface,
    ushort VendorId,
    ushort ProductId,
    ushort VersionNumber,
    ushort UsagePage,
    ushort Usage,
    ushort InputReportByteLength,
    ushort OutputReportByteLength,
    ushort FeatureReportByteLength,
    string? Manufacturer,
    string? Product,
    string? SerialNumber,
    string? Error);
