using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ControlWorksCollector.Native
{
    /// <summary>
    /// P/Invoke-обёртки: сетевые подключения (mpr.dll) и DPAPI-шифрование пароля (crypt32.dll).
    /// </summary>
    internal static class Win32
    {
        // ---------- WNet ----------

        [StructLayout(LayoutKind.Sequential)]
        public struct NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpLocalName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpRemoteName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpComment;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpProvider;
        }

        public const int RESOURCETYPE_DISK = 0x00000001;

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        public static extern int WNetAddConnection2(ref NETRESOURCE netResource, string password, string username, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        public static extern int WNetCancelConnection2(string name, int flags, bool force);

        // ---------- DPAPI ----------

        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        /// <summary>Шифрует строку средствами DPAPI и возвращает Base64.</summary>
        public static string Protect(string plain)
        {
            byte[] data = Encoding.UTF8.GetBytes(plain ?? "");
            byte[] enc = ProtectBytes(data);
            return Convert.ToBase64String(enc);
        }

        /// <summary>Расшифровывает Base64, полученный из Protect.</summary>
        public static string Unprotect(string protectedBase64)
        {
            if (string.IsNullOrEmpty(protectedBase64))
            {
                return "";
            }
            byte[] data = Convert.FromBase64String(protectedBase64);
            byte[] dec = UnprotectBytes(data);
            return Encoding.UTF8.GetString(dec);
        }

        private static byte[] ProtectBytes(byte[] data)
        {
            DATA_BLOB inBlob = ToBlob(data);
            DATA_BLOB outBlob;
            try
            {
                if (!CryptProtectData(ref inBlob, "ControlWorksCollector", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out outBlob))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось зашифровать пароль (DPAPI).");
                }
                try { return FromBlob(outBlob); }
                finally { LocalFree(outBlob.pbData); }
            }
            finally { FreeBlob(inBlob); }
        }

        private static byte[] UnprotectBytes(byte[] data)
        {
            DATA_BLOB inBlob = ToBlob(data);
            DATA_BLOB outBlob;
            try
            {
                if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out outBlob))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось расшифровать пароль (DPAPI).");
                }
                try { return FromBlob(outBlob); }
                finally { LocalFree(outBlob.pbData); }
            }
            finally { FreeBlob(inBlob); }
        }

        private static DATA_BLOB ToBlob(byte[] data)
        {
            DATA_BLOB blob = new DATA_BLOB();
            blob.cbData = data.Length;
            if (data.Length == 0)
            {
                blob.pbData = IntPtr.Zero;
            }
            else
            {
                blob.pbData = Marshal.AllocHGlobal(data.Length);
                Marshal.Copy(data, 0, blob.pbData, data.Length);
            }
            return blob;
        }

        private static void FreeBlob(DATA_BLOB blob)
        {
            if (blob.pbData != IntPtr.Zero)
            {
                LocalFree(blob.pbData);
            }
        }

        private static byte[] FromBlob(DATA_BLOB blob)
        {
            byte[] result = new byte[blob.cbData];
            if (blob.cbData > 0)
            {
                Marshal.Copy(blob.pbData, result, 0, blob.cbData);
            }
            return result;
        }

        // ---------- NetWkstaGetInfo: имя удалённого компьютера ----------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WKSTA_INFO_100
        {
            public int wki100_platform_id;
            [MarshalAs(UnmanagedType.LPWStr)] public string wki100_computername;
            [MarshalAs(UnmanagedType.LPWStr)] public string wki100_langroup;
            public int wki100_ver_major;
            public int wki100_ver_minor;
        }

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetWkstaGetInfo(string servername, int level, out IntPtr bufPtr);

        [DllImport("netapi32.dll")]
        private static extern int NetApiBufferFree(IntPtr buffer);

        /// <summary>Возвращает имя компьютера по адресу/имени (через установленную сессию). Пусто при неудаче.</summary>
        public static string GetRemoteComputerName(string host)
        {
            IntPtr buf = IntPtr.Zero;
            try
            {
                int rc = NetWkstaGetInfo(@"\\" + host, 100, out buf);
                if (rc != 0 || buf == IntPtr.Zero)
                {
                    return "";
                }
                WKSTA_INFO_100 info = (WKSTA_INFO_100)Marshal.PtrToStructure(buf, typeof(WKSTA_INFO_100));
                return info.wki100_computername ?? "";
            }
            catch
            {
                return "";
            }
            finally
            {
                if (buf != IntPtr.Zero)
                {
                    NetApiBufferFree(buf);
                }
            }
        }

        // ---------- SendARP: MAC-адрес по IP ----------

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(int destIp, int srcIp, byte[] pMacAddr, ref int phyAddrLen);

        /// <summary>Возвращает MAC-адрес (AA-BB-CC-DD-EE-FF) для IP в локальной подсети. Пусто при неудаче.</summary>
        public static string GetMacFromArp(string ip)
        {
            try
            {
                System.Net.IPAddress addr;
                if (!System.Net.IPAddress.TryParse((ip ?? "").Trim(), out addr))
                {
                    return "";
                }
                byte[] mac = new byte[6];
                int len = mac.Length;
                int r = SendARP(BitConverter.ToInt32(addr.GetAddressBytes(), 0), 0, mac, ref len);
                if (r != 0 || len == 0)
                {
                    return "";
                }
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < len; i++)
                {
                    if (i > 0)
                    {
                        sb.Append('-');
                    }
                    sb.Append(mac[i].ToString("X2"));
                }
                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }
    }
}
