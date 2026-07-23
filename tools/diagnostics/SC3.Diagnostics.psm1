Set-StrictMode -Version Latest

function Initialize-SC3CoreAudioInterop {
    if ('SC3Diagnostics.CoreAudio' -as [type]) {
        return
    }

    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SC3Diagnostics
{
    public enum EDataFlow { Render = 0, Capture = 1, All = 2 }

    [Flags]
    public enum DeviceState : uint
    {
        Active = 0x1,
        Disabled = 0x2,
        NotPresent = 0x4,
        Unplugged = 0x8,
        All = 0xF
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
        public PropertyKey(Guid formatId, int propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public IntPtr PointerValue;

        public string GetString()
        {
            return (VariantType == 31 && PointerValue != IntPtr.Zero)
                ? Marshal.PtrToStringUni(PointerValue)
                : null;
        }
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);
        [PreserveSig]
        int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid interfaceId, uint classContext, IntPtr activationParameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig]
        int OpenPropertyStore(uint accessMode, out IPropertyStore properties);
        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig]
        int GetState(out DeviceState state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint count);
        [PreserveSig]
        int GetAt(uint index, out PropertyKey key);
        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig]
        int Commit();
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig]
        int GetChannelCount(out uint channelCount);
        [PreserveSig]
        int SetMasterVolumeLevel(float levelDb, IntPtr eventContext);
        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, IntPtr eventContext);
        [PreserveSig]
        int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig]
        int SetChannelVolumeLevel(uint channel, float levelDb, IntPtr eventContext);
        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr eventContext);
        [PreserveSig]
        int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr eventContext);
        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig]
        int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig]
        int VolumeStepUp(IntPtr eventContext);
        [PreserveSig]
        int VolumeStepDown(IntPtr eventContext);
        [PreserveSig]
        int QueryHardwareSupport(out uint hardwareSupportMask);
        [PreserveSig]
        int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject { }

    public sealed class AudioEndpointInfo
    {
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public string Flow { get; internal set; }
        public string State { get; internal set; }
        public int ChannelCount { get; internal set; }
        public float? VolumePercent { get; internal set; }
        public bool? Muted { get; internal set; }
        public float[] ChannelVolumePercent { get; internal set; }
        public string Error { get; internal set; }
    }

    public static class CoreAudio
    {
        private const uint CLSCTX_ALL = 23;
        private const uint STGM_READ = 0;
        private static readonly Guid EndpointVolumeId = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        private static readonly PropertyKey FriendlyNameKey = new PropertyKey(
            new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);

        public static AudioEndpointInfo[] Enumerate()
        {
            var result = new List<AudioEndpointInfo>();
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            try
            {
                EnumerateFlow(enumerator, EDataFlow.Render, result);
                EnumerateFlow(enumerator, EDataFlow.Capture, result);
            }
            finally
            {
                Marshal.FinalReleaseComObject(enumerator);
            }
            return result.ToArray();
        }

        private static void EnumerateFlow(IMMDeviceEnumerator enumerator, EDataFlow flow, List<AudioEndpointInfo> result)
        {
            IMMDeviceCollection collection;
            enumerator.EnumAudioEndpoints(flow, DeviceState.All, out collection);
            try
            {
                uint count;
                Marshal.ThrowExceptionForHR(collection.GetCount(out count));
                for (uint i = 0; i < count; i++)
                {
                    IMMDevice device;
                    Marshal.ThrowExceptionForHR(collection.Item(i, out device));
                    try { result.Add(ReadDevice(device, flow)); }
                    finally { Marshal.FinalReleaseComObject(device); }
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(collection);
            }
        }

        private static AudioEndpointInfo ReadDevice(IMMDevice device, EDataFlow flow)
        {
            var info = new AudioEndpointInfo { Flow = flow.ToString(), ChannelVolumePercent = new float[0] };
            try
            {
                string id;
                DeviceState state;
                Marshal.ThrowExceptionForHR(device.GetId(out id));
                Marshal.ThrowExceptionForHR(device.GetState(out state));
                info.Id = id;
                info.State = state.ToString();

                IPropertyStore store;
                Marshal.ThrowExceptionForHR(device.OpenPropertyStore(STGM_READ, out store));
                try
                {
                    var key = FriendlyNameKey;
                    PropVariant value;
                    Marshal.ThrowExceptionForHR(store.GetValue(ref key, out value));
                    try { info.Name = value.GetString(); }
                    finally { PropVariantClear(ref value); }
                }
                finally { Marshal.FinalReleaseComObject(store); }

                if ((state & DeviceState.Active) == DeviceState.Active)
                {
                    object activated;
                    var iid = EndpointVolumeId;
                    Marshal.ThrowExceptionForHR(device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out activated));
                    var endpointVolume = (IAudioEndpointVolume)activated;
                    try
                    {
                        uint channelCount;
                        float master;
                        bool muted;
                        Marshal.ThrowExceptionForHR(endpointVolume.GetChannelCount(out channelCount));
                        Marshal.ThrowExceptionForHR(endpointVolume.GetMasterVolumeLevelScalar(out master));
                        Marshal.ThrowExceptionForHR(endpointVolume.GetMute(out muted));
                        info.ChannelCount = checked((int)channelCount);
                        info.VolumePercent = master * 100.0f;
                        info.Muted = muted;
                        var channels = new float[channelCount];
                        for (uint channel = 0; channel < channelCount; channel++)
                        {
                            float channelVolume;
                            Marshal.ThrowExceptionForHR(endpointVolume.GetChannelVolumeLevelScalar(channel, out channelVolume));
                            channels[channel] = channelVolume * 100.0f;
                        }
                        info.ChannelVolumePercent = channels;
                    }
                    finally { Marshal.FinalReleaseComObject(activated); }
                }
            }
            catch (Exception ex)
            {
                info.Error = ex.Message;
            }
            return info;
        }
    }
}
'@
}

function Get-SC3PnpBaseRecords {
    [CmdletBinding()]
    param([switch] $PresentOnly)

    $pnpCommand = Get-Command Get-PnpDevice -ErrorAction SilentlyContinue
    if (-not $pnpCommand) {
        return @(Get-CimInstance Win32_PnPEntity | ForEach-Object {
            [pscustomobject] [ordered]@{
                FriendlyName      = $_.Name
                InstanceId       = $_.PNPDeviceID
                Class            = $_.PNPClass
                Status           = $_.Status
                Present          = $null
                Problem          = $_.ConfigManagerErrorCode
                Manufacturer     = $_.Manufacturer
                Service          = $_.Service
                HardwareIds      = @($_.HardwareID)
                CompatibleIds    = @($_.CompatibleID)
                Parent           = $null
                BusDescription   = $null
                Location         = $null
                LocationPaths    = @()
                ContainerId      = $null
                ClassGuid        = $_.ClassGuid
                DriverVersion    = $null
                DriverDate       = $null
                Enumerator       = $null
            }
        })
    }

    $parameters = @{}
    if ($PresentOnly) { $parameters.PresentOnly = $true }
    return @(Get-PnpDevice @parameters | ForEach-Object {
        [pscustomobject] [ordered]@{
            FriendlyName      = $_.FriendlyName
            InstanceId       = $_.InstanceId
            Class            = $_.Class
            Status           = $_.Status
            Present          = $_.Present
            Problem          = $_.Problem
            Manufacturer     = $null
            Service          = $null
            HardwareIds      = @()
            CompatibleIds    = @()
            Parent           = $null
            BusDescription   = $null
            Location         = $null
            LocationPaths    = @()
            ContainerId      = $null
            ClassGuid        = $null
            DriverVersion    = $null
            DriverDate       = $null
            Enumerator       = $null
        }
    })
}

function Expand-SC3PnpRecord {
    param([Parameter(Mandatory)] $Record)

    try {
        $propertyMap = @{}
        foreach ($property in @(Get-PnpDeviceProperty -InstanceId $Record.InstanceId -ErrorAction Stop)) {
            $propertyMap[$property.KeyName] = $property.Data
        }
        foreach ($mapping in @{
            Manufacturer   = 'DEVPKEY_Device_Manufacturer'
            Service        = 'DEVPKEY_Device_Service'
            HardwareIds    = 'DEVPKEY_Device_HardwareIds'
            CompatibleIds  = 'DEVPKEY_Device_CompatibleIds'
            Parent         = 'DEVPKEY_Device_Parent'
            BusDescription = 'DEVPKEY_Device_BusReportedDeviceDesc'
            Location       = 'DEVPKEY_Device_LocationInfo'
            LocationPaths  = 'DEVPKEY_Device_LocationPaths'
            ContainerId    = 'DEVPKEY_Device_ContainerId'
            ClassGuid      = 'DEVPKEY_Device_ClassGuid'
            DriverVersion  = 'DEVPKEY_Device_DriverVersion'
            DriverDate     = 'DEVPKEY_Device_DriverDate'
            Enumerator     = 'DEVPKEY_Device_EnumeratorName'
        }.GetEnumerator()) {
            if ($propertyMap.ContainsKey($mapping.Value)) {
                $Record.($mapping.Key) = $propertyMap[$mapping.Value]
            }
        }
    }
    catch {
        # A device can disappear between enumeration and property lookup.
    }
    return $Record
}

function Get-SC3CoreAudioEndpoints {
    [CmdletBinding()]
    param()

    Initialize-SC3CoreAudioInterop
    return @([SC3Diagnostics.CoreAudio]::Enumerate() | ForEach-Object {
        [pscustomobject] [ordered]@{
            Name                 = $_.Name
            Id                   = $_.Id
            Flow                 = $_.Flow
            State                = $_.State
            ChannelCount         = $_.ChannelCount
            VolumePercent        = if ($null -eq $_.VolumePercent) { $null } else { [math]::Round($_.VolumePercent, 2) }
            Muted                = $_.Muted
            ChannelVolumePercent = @($_.ChannelVolumePercent | ForEach-Object { [math]::Round($_, 2) })
            Error                = $_.Error
        }
    })
}

function Test-SC3TextMatch {
    param(
        [Parameter(Mandatory)] $InputObject,
        [Parameter(Mandatory)] [string] $Pattern
    )

    $text = @(
        $InputObject.FriendlyName, $InputObject.InstanceId, $InputObject.Manufacturer,
        $InputObject.BusDescription, $InputObject.HardwareIds, $InputObject.CompatibleIds
    ) -join '|'
    return $text -match $Pattern
}

function Get-SC3Inventory {
    [CmdletBinding()]
    param(
        [string] $Match = 'FIFINE|Mixer SC3|\bSC3\b',
        [switch] $PresentOnly
    )

    $allPnp = @(Get-SC3PnpBaseRecords -PresentOnly:$PresentOnly)
    $directMatches = @($allPnp | Where-Object { Test-SC3TextMatch $_ $Match })

    $usbIds = @($directMatches | ForEach-Object {
        @($_.InstanceId) | ForEach-Object {
            if ($_ -match '(?i)(VID_[0-9A-F]{4}&PID_[0-9A-F]{4})') { $matches[1].ToUpperInvariant() }
        }
    } | Sort-Object -Unique)

    $relatedBase = @($allPnp | Where-Object {
        $record = $_
        if (Test-SC3TextMatch $record $Match) { return $true }
        foreach ($usbId in $usbIds) {
            $recordText = @($record.InstanceId, $record.Parent, $record.HardwareIds, $record.CompatibleIds) -join '|'
            if ($recordText -match [regex]::Escape($usbId)) { return $true }
        }
        return $false
    } | Sort-Object Class, FriendlyName, InstanceId -Unique)
    $related = @($relatedBase | ForEach-Object { Expand-SC3PnpRecord $_ })

    $audio = @(Get-SC3CoreAudioEndpoints)
    $matchingAudio = @($audio | Where-Object { (($_.Name, $_.Id) -join '|') -match $Match })

    $serial = @(Get-CimInstance Win32_SerialPort -ErrorAction SilentlyContinue | Where-Object {
        $text = @($_.Name, $_.Description, $_.DeviceID, $_.PNPDeviceID) -join '|'
        ($text -match $Match) -or ($usbIds | Where-Object { $text -match [regex]::Escape($_) })
    } | Select-Object Name, Description, DeviceID, PNPDeviceID, ProviderType, Status)

    [pscustomobject] [ordered]@{
        CapturedAt          = (Get-Date).ToString('o')
        ComputerName        = $env:COMPUTERNAME
        MatchPattern        = $Match
        PresentOnly         = [bool]$PresentOnly
        UsbVidPid           = $usbIds
        Devices             = $related
        UsbDevices          = @($related | Where-Object { $_.Enumerator -eq 'USB' -or $_.InstanceId -match '^USB\\' })
        AudioEndpoints      = $matchingAudio
        AudioInterfaces     = @($related | Where-Object { $_.Class -in @('AudioEndpoint', 'Media', 'MEDIA') })
        HidInterfaces       = @($related | Where-Object { $_.Class -eq 'HIDClass' -or $_.InstanceId -match '^HID\\' })
        MidiCandidates      = @($related | Where-Object {
            $_.Class -in @('Media', 'MEDIA') -and ((@($_.FriendlyName, $_.BusDescription, $_.Service) -join '|') -match 'MIDI')
        })
        SerialPorts         = $serial
        AllAudioEndpoints   = $audio
    }
}

function Get-SC3Snapshot {
    [CmdletBinding()]
    param([string] $Match = 'FIFINE|Mixer SC3|\bSC3\b')

    # Monitoring uses the lightweight PnP view. Detailed properties and CIM
    # serial-port queries belong to the one-shot inventory and are too slow for polling.
    $allPnp = @(Get-SC3PnpBaseRecords -PresentOnly)
    $directMatches = @($allPnp | Where-Object { Test-SC3TextMatch $_ $Match })
    $usbIds = @($directMatches | ForEach-Object {
        if ($_.InstanceId -match '(?i)(VID_[0-9A-F]{4}&PID_[0-9A-F]{4})') {
            $matches[1].ToUpperInvariant()
        }
    } | Sort-Object -Unique)
    $devices = @($allPnp | Where-Object {
        $record = $_
        if (Test-SC3TextMatch $record $Match) { return $true }
        foreach ($usbId in $usbIds) {
            if ($record.InstanceId -match [regex]::Escape($usbId)) { return $true }
        }
        return $false
    })
    $audioEndpoints = @(Get-SC3CoreAudioEndpoints | Where-Object { (($_.Name, $_.Id) -join '|') -match $Match })
    $rows = @()
    foreach ($device in $devices) {
        $rows += [pscustomobject]@{
            Kind        = 'PnP'
            Key         = $device.InstanceId
            Description = $device.FriendlyName
            State       = @($device.Status, $device.Present, $device.Problem, $device.DriverVersion) -join '|'
        }
    }
    foreach ($endpoint in $audioEndpoints) {
        $rows += [pscustomobject]@{
            Kind        = 'CoreAudio'
            Key         = $endpoint.Id
            Description = $endpoint.Name
            State       = @($endpoint.State, $endpoint.Muted, $endpoint.VolumePercent, ($endpoint.ChannelVolumePercent -join ',')) -join '|'
        }
    }
    return $rows
}

Export-ModuleMember -Function Get-SC3Inventory, Get-SC3CoreAudioEndpoints, Get-SC3Snapshot
