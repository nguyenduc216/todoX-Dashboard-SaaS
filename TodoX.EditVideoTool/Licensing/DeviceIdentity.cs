using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TodoX.EditVideoTool.Licensing;

public interface IDeviceIdentity
{
    /// <summary>Lowercase SHA-256 hex that identifies this machine to the license server.</summary>
    string DeviceHash { get; }
    string DeviceName { get; }
    string Os { get; }
}

/// <summary>
/// Hash of the Windows install id (MachineGuid) and the system volume serial. Both survive reboots and app
/// reinstalls but differ on a copied/cloned profile, which is what slot counting needs. The raw ids never
/// leave the machine — only the salted hash does.
/// </summary>
public sealed partial class WindowsDeviceIdentity : IDeviceIdentity
{
    private const string Salt = "todox-editvideo/device/v1";

    public WindowsDeviceIdentity()
    {
        DeviceHash = ComputeHash(ReadMachineGuid(), ReadSystemVolumeSerial());
    }

    public string DeviceHash { get; }
    public string DeviceName => Environment.MachineName;
    public string Os => RuntimeInformation.OSDescription;

    internal static string ComputeHash(string machineGuid, string volumeSerial)
    {
        var bytes = Encoding.UTF8.GetBytes($"{Salt}|{machineGuid.Trim().ToLowerInvariant()}|{volumeSerial.Trim().ToLowerInvariant()}");
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string ReadMachineGuid()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("TodoX EditVideoTool currently supports Windows only.");
        }

        using var key = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)
            .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return key?.GetValue("MachineGuid") as string
            ?? throw new InvalidOperationException("Windows MachineGuid is not available.");
    }

    private static string ReadSystemVolumeSerial()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        return GetVolumeInformationW(root, null, 0, out var serial, out _, out _, null, 0)
            ? serial.ToString("x8")
            : "unknown";
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformationW(
        string rootPathName, char[]? volumeNameBuffer, int volumeNameSize, out uint volumeSerialNumber,
        out uint maximumComponentLength, out uint fileSystemFlags, char[]? fileSystemNameBuffer, int fileSystemNameSize);
}
