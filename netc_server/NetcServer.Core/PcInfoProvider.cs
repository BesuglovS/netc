using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetcShared;

namespace NetcServer.Core;

/// <summary>Сбор сведений об ученическом ПК.</summary>
public static class PcInfoProvider
{
    public static PcInfo Get()
    {
        var info = new PcInfo
        {
            MachineName = Environment.MachineName,
            UserName = SessionUser.GetActiveUserName(),
            OsVersion = Environment.OSVersion.VersionString,
            AgentVersion = typeof(PcInfoProvider).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            MacAddress = GetMac(),
            Manufacturer = GetBios("SystemManufacturer"),
            Model = GetBios("SystemProductName")
        };

        try
        {
            foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    info.Addresses.Add(ip.ToString());
                }
            }
        }
        catch { /* адреса не критичны */ }

        return info;
    }

    /// <summary>Значение из BIOS-раздела реестра (SystemManufacturer / SystemProductName).</summary>
    private static string GetBios(string valueName)
    {
        try
        {
#pragma warning disable CA1416 // реестр — только Windows; NetcServer.Core используется лишь агентом/службой под Windows
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            return key?.GetValue(valueName)?.ToString() ?? "";
#pragma warning restore CA1416
        }
        catch { return ""; }
    }

    /// <summary>MAC основного (поднятого, не loopback) интерфейса — для Wake-on-LAN.
    /// Предпочитаем интерфейс со шлюзом по умолчанию, чтобы не взять MAC виртуального адаптера
    /// (VPN/Hyper-V), иначе WoL уйдёт «в никуда».</summary>
    private static string GetMac()
    {
        try
        {
            byte[]? fallback = null;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.OperationalStatus != OperationalStatus.Up) continue;

                var bytes = ni.GetPhysicalAddress()?.GetAddressBytes();
                if (bytes is null || bytes.Length != 6) continue;

                if (HasDefaultGateway(ni)) return FormatMac(bytes);
                fallback ??= bytes;
            }
            if (fallback is not null) return FormatMac(fallback);
        }
        catch { }
        return "";
    }

    private static bool HasDefaultGateway(NetworkInterface ni)
    {
        try
        {
            foreach (var gw in ni.GetIPProperties().GatewayAddresses)
            {
                if (gw?.Address is { } a && a.AddressFamily == AddressFamily.InterNetwork) return true;
            }
        }
        catch { }
        return false;
    }

    private static string FormatMac(byte[] bytes) => string.Join("-", bytes.Select(b => b.ToString("X2")));
}
