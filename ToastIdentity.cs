using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.UI.Notifications.Management;

namespace NotifyIsland;

internal static class ToastIdentity
{
    public static string PackageLine { get; private set; } = "unpackaged";
    public static string LastAccess { get; private set; } = "";

    public static string ProbePackage()
    {
        try
        {
            var pkg = Windows.ApplicationModel.Package.Current;
            var id = pkg.Id;
            PackageLine = "packaged " + id.FamilyName + " " + id.Version.Major + "." + id.Version.Minor;
        }
        catch (Exception ex)
        {
            PackageLine = "unpackaged (" + ex.GetType().Name + ")";
        }
        return PackageLine;
    }

    public static async Task<UserNotificationListenerAccessStatus> RequestAccessLoggedAsync()
    {
        var listener = UserNotificationListener.Current;
        var access = await listener.RequestAccessAsync();
        LastAccess = access.ToString();
        return access;
    }

    public static async Task<string> TryRegisterSparseAsync()
    {
        var dir = AppContext.BaseDirectory;
        var manifest = Path.Combine(dir, "pack", "sparse", "AppxManifest.xml");
        if (!File.Exists(manifest))
            return "No sparse manifest at " + manifest;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Path '" +
                            manifest.Replace("'", "''") + "' -Register -ExternalLocation '" +
                            dir.TrimEnd('\\').Replace("'", "''") + "'\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return "PowerShell failed to start";
            var stdout = await p.StandardOutput.ReadToEndAsync();
            var stderr = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            var text = (stdout + " " + stderr).Trim();
            if (p.ExitCode != 0)
                return "Add-AppxPackage exit " + p.ExitCode + " " + text;
            ProbePackage();
            return "Registered. Identity now: " + PackageLine;
        }
        catch (Exception ex)
        {
            return "Register threw " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    public static void OpenPrivacySettings()
    {
        foreach (var uri in new[] { "ms-settings:privacy-notifications", "ms-settings:notifications" })
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                return;
            }
            catch
            {
            }
        }
    }
}
