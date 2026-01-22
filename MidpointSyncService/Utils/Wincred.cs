using System.Runtime.InteropServices;
using System.Text;

public static class WinCred
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", SetLastError = true)]
    private static extern void CredFree([In] IntPtr cred);

    private const uint CRED_TYPE_GENERIC = 1;

    /// <summary>
    /// Reads a Generic Credential from Windows Credential Manager.
    /// Returns (user, secret). Nulls if not found.
    /// </summary>
    public static (string? user, string? secret) ReadGeneric(string targetName)
    {
        if (!CredRead(targetName, CRED_TYPE_GENERIC, 0, out IntPtr pCred))
            return (null, null);

        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(pCred);
            string? user = cred.UserName;
            string? secret = null;

            if (cred.CredentialBlob != IntPtr.Zero && cred.CredentialBlobSize > 0)
            {
                byte[] blob = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, blob, 0, blob.Length);
                secret = Encoding.Unicode.GetString(blob).TrimEnd('\0');
            }

            return (user, secret);
        }
        finally
        {
            CredFree(pCred);
        }
    }
}
