using System.DirectoryServices.AccountManagement;

public static class ADSearch
{
    public static string[] dnList = RegistryReader.GetExceptionList();

    public static bool InExceptionList(string username)
    {
        if(dnList.Count() < 1)
            return false;

        try
        {
            using var context = new PrincipalContext(ContextType.Domain);
            
            var user = UserPrincipal.FindByIdentity(
                context, 
                IdentityType.SamAccountName, 
                username);

            if (user == null)
                return false;

            var splittedDn = user.DistinguishedName.Split(',', 2)[1];

            foreach(string dn in dnList)
            {
                if(dn.Equals(splittedDn))
                    return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            LogManager.Log($"[AdSearch] InExceptionList: Failed to get OU from user '{username}': {ex.Message}");
            return false;
        }
    }
}