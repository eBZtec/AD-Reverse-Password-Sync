using System.Security.Cryptography;
using System.Text;

public static class DPAPI
{
    public static bool DecryptBase64(string base64, out string? plaintext)
    {
        try
        {
            byte[] cipher = Convert.FromBase64String(base64);
            byte[] plainBytes = ProtectedData.Unprotect(cipher, optionalEntropy: null, scope: DataProtectionScope.LocalMachine);

            string msg = Encoding.Unicode.GetString(plainBytes);

            plaintext = msg.TrimEnd('\0');
            return true;
        }
        catch
        {
            plaintext = null;
            return false;
        }
    }
}
