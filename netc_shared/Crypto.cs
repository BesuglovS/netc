using System.Security.Cryptography;
using System.Text;

namespace NetcShared;

/// <summary>Производные ключа из общего секрета.</summary>
public static class NetcCrypto
{
    public static byte[] DeriveKey(string sharedKey)
        => SHA256.HashData(Encoding.UTF8.GetBytes("netc:" + (sharedKey ?? "")));
}
