using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SC3.HidMonitor;

internal static class HidNative
{
    internal const ushort Sc3VendorId = 0x3142;
    internal const ushort Sc3ProductId = 0x0C33;

    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    internal static IReadOnlyList<HidDeviceDescriptor> EnumerateSc3Devices()
    {
        HidD_GetHidGuid(out var hidGuid);
        var deviceInfoSet = SetupDiGetClassDevs(
            ref hidGuid,
            null,
            IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);

        if (deviceInfoSet == InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs failed.");
        }

        try
        {
            var devices = new List<HidDeviceDescriptor>();
            for (uint index = 0; ; index++)
            {
                var interfaceData = new SpDeviceInterfaceData
                {
                    Size = (uint)Marshal.SizeOf<SpDeviceInterfaceData>()
                };

                if (!SetupDiEnumDeviceInterfaces(
                        deviceInfoSet,
                        IntPtr.Zero,
                        ref hidGuid,
                        index,
                        ref interfaceData))
                {
                    const int noMoreItems = 259;
                    var error = Marshal.GetLastWin32Error();
                    if (error == noMoreItems)
                    {
                        break;
                    }

                    throw new Win32Exception(error, "SetupDiEnumDeviceInterfaces failed.");
                }

                var path = GetDevicePath(deviceInfoSet, ref interfaceData);

                // Do not even open unrelated HID paths (keyboards, mice, etc.).
                if (!IsSc3Path(path))
                {
                    continue;
                }

                devices.Add(ReadDescriptor(path));
            }

            return devices
                .OrderBy(device => device.Interface, StringComparer.OrdinalIgnoreCase)
                .ThenBy(device => device.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _ = SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    internal static SafeFileHandle OpenForInput(string path)
    {
        if (!IsSc3Path(path))
        {
            throw new InvalidOperationException("Refusing to open a HID path outside the SC3 VID/PID filter.");
        }

        var handle = CreateFile(
            path,
            GenericRead,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOverlapped,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Unable to open the SC3 HID interface for input.");
        }

        var attributes = CreateAttributes();
        if (!HidD_GetAttributes(handle, ref attributes)
            || attributes.VendorId != Sc3VendorId
            || attributes.ProductId != Sc3ProductId)
        {
            handle.Dispose();
            throw new InvalidOperationException("The opened HID handle did not confirm the SC3 VID/PID.");
        }

        return handle;
    }

    private static HidDeviceDescriptor ReadDescriptor(string path)
    {
        using var handle = CreateFile(
            path,
            0,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return ErrorDescriptor(path, new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }

        var attributes = CreateAttributes();
        if (!HidD_GetAttributes(handle, ref attributes))
        {
            return ErrorDescriptor(path, new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }

        if (attributes.VendorId != Sc3VendorId || attributes.ProductId != Sc3ProductId)
        {
            // This should not happen after the path filter. Keep the defense at the API boundary.
            return ErrorDescriptor(path, "VID/PID reported by HidD_GetAttributes did not match the SC3.");
        }

        var caps = default(HidPCaps);
        string? capsError = null;
        if (!HidD_GetPreparsedData(handle, out var preparsedData))
        {
            capsError = new Win32Exception(Marshal.GetLastWin32Error()).Message;
        }
        else
        {
            try
            {
                var status = HidP_GetCaps(preparsedData, out caps);
                if (status != 0x00110000)
                {
                    capsError = $"HidP_GetCaps failed with NTSTATUS 0x{status:X8}.";
                }
            }
            finally
            {
                _ = HidD_FreePreparsedData(preparsedData);
            }
        }

        return new HidDeviceDescriptor(
            path,
            GetInterfaceName(path),
            attributes.VendorId,
            attributes.ProductId,
            attributes.VersionNumber,
            caps.UsagePage,
            caps.Usage,
            caps.InputReportByteLength,
            caps.OutputReportByteLength,
            caps.FeatureReportByteLength,
            ReadString(handle, HidD_GetManufacturerString),
            ReadString(handle, HidD_GetProductString),
            ReadString(handle, HidD_GetSerialNumberString),
            capsError);
    }

    private static HidDeviceDescriptor ErrorDescriptor(string path, string error) => new(
        path,
        GetInterfaceName(path),
        Sc3VendorId,
        Sc3ProductId,
        0,
        0,
        0,
        0,
        0,
        0,
        null,
        null,
        null,
        error);

    private static HidAttributes CreateAttributes() => new()
    {
        Size = Marshal.SizeOf<HidAttributes>()
    };

    private static string? ReadString(SafeFileHandle handle, HidStringReader reader)
    {
        var buffer = new byte[512];
        return reader(handle, buffer, buffer.Length)
            ? Encoding.Unicode.GetString(buffer).TrimEnd('\0')
            : null;
    }

    private static string GetDevicePath(IntPtr deviceInfoSet, ref SpDeviceInterfaceData interfaceData)
    {
        _ = SetupDiGetDeviceInterfaceDetail(
            deviceInfoSet,
            ref interfaceData,
            IntPtr.Zero,
            0,
            out var requiredSize,
            IntPtr.Zero);

        const int insufficientBuffer = 122;
        var sizeError = Marshal.GetLastWin32Error();
        if (requiredSize == 0 || sizeError != insufficientBuffer)
        {
            throw new Win32Exception(sizeError, "Unable to determine HID interface path size.");
        }

        var detailBuffer = Marshal.AllocHGlobal(checked((int)requiredSize));
        try
        {
            Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref interfaceData,
                    detailBuffer,
                    requiredSize,
                    out _,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read HID interface path.");
            }

            return Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, 4))
                ?? throw new InvalidOperationException("SetupAPI returned an empty HID interface path.");
        }
        finally
        {
            Marshal.FreeHGlobal(detailBuffer);
        }
    }

    private static bool IsSc3Path(string path) =>
        path.Contains("vid_3142&pid_0c33", StringComparison.OrdinalIgnoreCase);

    private static string GetInterfaceName(string path)
    {
        var marker = path.IndexOf("&mi_", StringComparison.OrdinalIgnoreCase);
        if (marker < 0 || path.Length < marker + 6)
        {
            return "unknown";
        }

        return $"MI_{path.Substring(marker + 4, 2).ToUpperInvariant()}";
    }

    private delegate bool HidStringReader(SafeFileHandle handle, byte[] buffer, int bufferLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        internal uint Size;
        internal Guid InterfaceClassGuid;
        internal uint Flags;
        internal IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidAttributes
    {
        internal int Size;
        internal ushort VendorId;
        internal ushort ProductId;
        internal ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidPCaps
    {
        internal ushort Usage;
        internal ushort UsagePage;
        internal ushort InputReportByteLength;
        internal ushort OutputReportByteLength;
        internal ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        internal ushort[] Reserved;

        internal ushort NumberLinkCollectionNodes;
        internal ushort NumberInputButtonCaps;
        internal ushort NumberInputValueCaps;
        internal ushort NumberInputDataIndices;
        internal ushort NumberOutputButtonCaps;
        internal ushort NumberOutputValueCaps;
        internal ushort NumberOutputDataIndices;
        internal ushort NumberFeatureButtonCaps;
        internal ushort NumberFeatureValueCaps;
        internal ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetAttributes(SafeFileHandle device, ref HidAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, out HidPCaps capabilities);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetManufacturerString(SafeFileHandle device, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetProductString(SafeFileHandle device, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetSerialNumberString(SafeFileHandle device, byte[] buffer, int bufferLength);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr parentWindow,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);
}
