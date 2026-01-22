using Microsoft.Win32;
using System;
using System.Linq;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: LsaNotifyHelper.exe <add|remove> <PackageName>");
            return 1;
        }

        string action = args[0].Trim().ToLowerInvariant();
        string package = args[1].Trim();

        if (string.IsNullOrWhiteSpace(package))
        {
            Console.Error.WriteLine("PackageName cannot be empty.");
            return 1;
        }

        try
        {
            using var lsaKeyWritable = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa", writable: true);
            if (lsaKeyWritable is null)
            {
                Console.Error.WriteLine("Cannot open LSA key (it may not exist).");
                return 3;
            }

            string[] current;
            var value = lsaKeyWritable.GetValue("Notification Packages", null);
            switch (value)
            {
                case string[] multi:
                    current = multi;
                    break;

                case string single:
                    current = new[] { single };
                    break;

                default:
                    current = Array.Empty<string>();
                    break;
            }

            bool exists = false;
            foreach (var notiPack in current)
            {
                if (string.Equals(notiPack, package, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }

            string[] updated;
            switch (action)
            {
                case "add":
                    if (exists)
                    {
                        updated = current;
                        break;
                    }
                    var list = new List<string>(current){package};
                    updated = list.ToArray();
                    break;

                case "remove":
                    var filtered = new List<string>();

                    foreach (var notiPack in current)
                    {
                        if (!string.Equals(notiPack, package, StringComparison.OrdinalIgnoreCase))
                        {
                            filtered.Add(notiPack);
                        }
                    }
                    updated = filtered.ToArray();
                    break;

                default:
                    throw new ArgumentException("First arg must be add or remove.");
            }

            lsaKeyWritable.SetValue("Notification Packages", updated, RegistryValueKind.MultiString);

            Console.WriteLine($"{action} OK. Packages now: {string.Join(", ", updated)}");
            Console.WriteLine("A reboot may be required for LSA to load the change.");
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Unauthorized. Run elevated (admin).");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.GetType().Name}: {ex.Message}");
            return 3;
        }
    }
}
