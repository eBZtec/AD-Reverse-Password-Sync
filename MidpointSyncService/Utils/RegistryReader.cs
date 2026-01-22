using Microsoft.Win32;

public static class RegistryReader
{
    private const string path = @"SOFTWARE\eBZ Tecnologia\AD-midPoint Sync";
    public static bool GetAdminBypass()
    {
        using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(path))
        {
            if (key == null)
            {
                LogManager.Log("[RegistryReader] GetSetOpBehavior: Registry Key(path) not found.");
                return false;
            }

            int? value = (int?)key.GetValue("admin_bypass");
            if (value == null)
            {
                LogManager.Log("[RegistryReader] GetSetOpBehavior: Value for admin_bypass not found.");
                return false;
            }

            if (value != 1)
                return false;
                
            return true;
        }
    }
    public static string GetMidpointURL()
    {
        using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(path))
        {
            if (key == null)
            {
                LogManager.Log("[RegistryReader] GetMidpointURL: Registry Key(path) not found.");
                return "null";
            }

            string? value = (string?)key.GetValue("midpoint_url");
            if (string.IsNullOrEmpty(value))
            {
                LogManager.Log("[RegistryReader] GetMidpointURL: Value for MidpointURL not found.");
                return "null";
            }

            if (value.EndsWith("/"))
                value = value.Remove(value.Length - 1);

            return value;
        }
    }
    
    public static bool AllowAllChanges()
    {
        using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(path))
        {
            if (key == null)
            {
                LogManager.Log("[RegistryReader] AllowAllChanges: Registry Key(path) not found.");
                return false;
            }

            int? value = (int?)key.GetValue("allow_all");
            if (value == null)
            {
                LogManager.Log("[RegistryReader] AllowAllChanges: Value for AllowAll not found.");
                return false;
            }

            if (value != 1)
                return false;
                
            return true;
        }
    }

    public static string GetMidpointAttribute()
    {
        using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(path))
        {
            if (key == null)
            {
                LogManager.Log("[RegistryReader] GetMidpointAttribute: Registry Key(path) not found.");
                return "name";
            }

            string? value = (string?)key.GetValue("midpoint_attribute");
            if (string.IsNullOrEmpty(value))
            {
                LogManager.Log("[RegistryReader] GetMidpointAttribute: Value for MidpointAttribute not found.");
                return "name";
            }

            return value;
        }
    }
}