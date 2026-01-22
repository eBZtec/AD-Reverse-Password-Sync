using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_SESSION = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;
    private const uint CRED_PERSIST_ENTERPRISE = 3;

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

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(ref CREDENTIAL userCredential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr pCredential);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    static int Main(string[] args)
    {
        // Usage:
        // CredHelper.exe add <TargetName> <UserName> <Password>
        // CredHelper.exe delete <TargetName>
        // CredHelper.exe show <TargetName>
        try
        {
            if (args.Length == 0) return Usage("Missing command.");

            var cmd = args[0].ToLowerInvariant();
            if (cmd == "add")
            {
                if (args.Length < 4) return Usage("add requires: target user pass");
                return Add(args[1], args[2], args[3]);
            }
            if (cmd == "delete")
            {
                if (args.Length < 2) return Usage("delete requires: target");
                return Delete(args[1]);
            }
            if (cmd == "show")
            {
                if (args.Length < 2) return Usage("show requires: target");
                return Show(args[1]);
            }

            return Usage($"Unknown command: {cmd}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 99;
        }
    }

    private static int Add(string target, string user, string pass)
    {
        var secretBytes = Encoding.Unicode.GetBytes(pass);

        IntPtr blob = IntPtr.Zero;
        try
        {
            blob = Marshal.AllocHGlobal(secretBytes.Length);
            Marshal.Copy(secretBytes, 0, blob, secretBytes.Length);

            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                UserName = user,
                CredentialBlobSize = (uint)secretBytes.Length,
                CredentialBlob = blob,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                Comment = "AD-midPoint Reverse Sync"
            };

            if (!CredWrite(ref cred, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            Console.WriteLine("OK");
            return 0;
        }
        finally
        {
            if (blob != IntPtr.Zero) Marshal.FreeHGlobal(blob);
            Array.Clear(secretBytes, 0, secretBytes.Length);
        }
    }

    private static int Delete(string target)
    {
        if (!CredDelete(target, CRED_TYPE_GENERIC, 0))
        {
            int err = Marshal.GetLastWin32Error();
            if (err == 1168) return 0; // not found => ok
            throw new Win32Exception(err);
        }
        Console.WriteLine("OK");
        return 0;
    }

    private static int Show(string target)
    {
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var pCred))
        {
            int err = Marshal.GetLastWin32Error();
            if (err == 1168)
            {
                Console.WriteLine($"NOT_FOUND target={target}");
                return 2;
            }
            throw new Win32Exception(err);
        }

        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(pCred);

            // Convert FILETIME -> DateTime (UTC)
            long ft = ((long)cred.LastWritten.dwHighDateTime << 32) | (uint)cred.LastWritten.dwLowDateTime;
            var lastWrittenUtc = ft > 0 ? DateTime.FromFileTimeUtc(ft) : (DateTime?)null;

            Console.WriteLine("FOUND");
            Console.WriteLine($"target={cred.TargetName}");
            Console.WriteLine($"type={cred.Type}"); // 1 = generic
            Console.WriteLine($"username={cred.UserName}");
            Console.WriteLine($"persist={PersistToString(cred.Persist)}");
            Console.WriteLine($"secret_bytes={cred.CredentialBlobSize}");
            Console.WriteLine($"last_written_utc={(lastWrittenUtc?.ToString("o") ?? "unknown")}");
            Console.WriteLine($"comment={cred.Comment}");

            // DO NOT print secret/password
            return 0;
        }
        finally
        {
            CredFree(pCred);
        }
    }

    private static string PersistToString(uint p) => p switch
    {
        CRED_PERSIST_SESSION => "session",
        CRED_PERSIST_LOCAL_MACHINE => "local_machine",
        CRED_PERSIST_ENTERPRISE => "enterprise",
        _ => $"unknown({p})"
    };

    private static int Usage(string msg)
    {
        Console.Error.WriteLine(msg);
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  CredHelper.exe add <TargetName> <UserName> <Password>");
        Console.Error.WriteLine("  CredHelper.exe delete <TargetName>");
        Console.Error.WriteLine("  CredHelper.exe show <TargetName>");
        return 1;
    }
}
