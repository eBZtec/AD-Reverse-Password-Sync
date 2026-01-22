using System.Runtime.InteropServices;

namespace AdPasswordBulkTester
{
    class Program
    {
        private const string HelpText = @"
        AD Password Bulk Tester

        Usage:
        AdPasswordBulkTester --csv <path> --domain <DNS domain> [--ou <LDAP OU DN>]
                            [--admin <user>] [--admin-pass <pass>]
                            [--expire-now] [--dry-run] [--delimiter ';' | ',']
                            [--stop-on-error]

        CSV format (header optional):
        samAccountName,oldPassword,newPassword
        Notes:
        - If oldPassword is empty, the program will attempt an ADMIN RESET via SetPassword(newPassword).
        - If oldPassword is present, it will attempt a USER CHANGE via ChangePassword(old,new).
        - --expire-now forces 'User must change at next logon' after success (reset mode only).
        ";

        static int Main(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            {
                Console.WriteLine(HelpText);
                return 1;
            }

            var options = ParseArgs(args);
            if (!options.TryGetValue("csv", out var csvPath) ||
                !options.TryGetValue("domain", out var domain))
            {
                Console.WriteLine("Missing required args --csv and/or --domain.\n");
                Console.WriteLine(HelpText);
                return 2;
            }

            var delimiter = options.TryGetValue("delimiter", out var del) && del.Length == 1 ? del[0] : ',';
            var ouDn = options.TryGetValue("ou", out var ou) ? ou : null;
            var adminUser = options.TryGetValue("admin", out var au) ? au : null;
            var adminPass = options.TryGetValue("admin-pass", out var ap) ? ap : null;
            var expireNow = options.ContainsKey("expire-now");
            var dryRun = options.ContainsKey("dry-run");
            var stopOnError = options.ContainsKey("stop-on-error");

            if (!File.Exists(csvPath))
            {
                Console.WriteLine($"CSV not found: {csvPath}");
                return 3;
            }

            // Build a PrincipalContext.
            // - If admin creds supplied: use them (reset mode usually needs privilege).
            // - If not: will use current logon context (Kerberos) on a domain-joined machine.
            PrincipalContext ctx = null;
            try
            {
                ctx = BuildContext(domain, ouDn, adminUser, adminPass);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create PrincipalContext: {ex.Message}");
                return 4;
            }

            var rows = LoadRows(csvPath, delimiter);
            if (rows.Count == 0)
            {
                Console.WriteLine("CSV is empty or only contained headers.");
                return 0;
            }

            int ok = 0, fail = 0, idx = 0;
            foreach (var r in rows)
            {
                idx++;
                var mode = string.IsNullOrEmpty(r.OldPassword) ? "RESET" : "CHANGE";
                Console.WriteLine($"[{idx}/{rows.Count}] {r.SamAccountName} -> {mode}");

                if (dryRun)
                {
                    Console.WriteLine("  (dry-run) Would execute operation.");
                    ok++;
                    continue;
                }

                try
                {
                    using var user = UserPrincipal.FindByIdentity(ctx, IdentityType.SamAccountName, r.SamAccountName);
                    if (user == null)
                    {
                        Console.WriteLine("  ERROR: User not found.");
                        fail++;
                        if (stopOnError) break;
                        continue;
                    }

                    if (!string.IsNullOrEmpty(r.OldPassword))
                    {
                        // USER CHANGE
                        user.ChangePassword(r.OldPassword, r.NewPassword);
                    }
                    else
                    {
                        // ADMIN RESET
                        user.SetPassword(r.NewPassword);
                        if (expireNow)
                        {
                            user.ExpirePasswordNow();
                            user.Save();
                        }
                    }

                    Console.WriteLine("  OK");
                    ok++;
                }
                catch (PasswordException pex)
                {
                    Console.WriteLine($"  FAIL (PasswordException): {pex.Message}");
                    ExplainCommonAdsiErrors(pex);
                    fail++; if (stopOnError) break;
                }
                catch (PrincipalOperationException poex)
                {
                    Console.WriteLine($"  FAIL (PrincipalOperation): {poex.Message}");
                    ExplainCommonAdsiErrors(poex);
                    fail++; if (stopOnError) break;
                }
                catch (COMException comex)
                {
                    Console.WriteLine($"  FAIL (COM 0x{(uint)comex.ErrorCode:X8}): {comex.Message}");
                    ExplainCommonAdsiErrors(comex);
                    fail++; if (stopOnError) break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  FAIL: {ex.GetType().Name}: {ex.Message}");
                    fail++; if (stopOnError) break;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Summary: OK={ok} FAIL={fail} TOTAL={rows.Count}");
            return fail == 0 ? 0 : 5;
        }

        private static PrincipalContext BuildContext(string domain, string? ouDn, string? adminUser, string? adminPass)
        {
            // If OU DN is specified, scope the search/ops there.
            // Example OU DN: OU=Users,OU=Corp,DC=example,DC=com
            if (!string.IsNullOrWhiteSpace(adminUser))
            {
                return string.IsNullOrWhiteSpace(ouDn)
                    ? new PrincipalContext(ContextType.Domain, domain, null, ContextOptions.Negotiate, adminUser, adminPass)
                    : new PrincipalContext(ContextType.Domain, domain, ouDn, ContextOptions.Negotiate, adminUser, adminPass);
            }

            return string.IsNullOrWhiteSpace(ouDn)
                ? new PrincipalContext(ContextType.Domain, domain)
                : new PrincipalContext(ContextType.Domain, domain, ouDn);
        }

        private sealed record Row(string SamAccountName, string OldPassword, string NewPassword);

        private static List<Row> LoadRows(string csvPath, char delimiter)
        {
            var rows = new List<Row>();
            foreach (var line in File.ReadLines(csvPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Allow optional header
                if (rows.Count == 0 && line.Trim().StartsWith("samAccountName", StringComparison.OrdinalIgnoreCase))
                    continue;

                var parts = SplitCsvLine(line, delimiter);
                if (parts.Length < 3)
                {
                    // If only 2 columns, treat as reset: sam,new
                    if (parts.Length == 2)
                    {
                        rows.Add(new Row(parts[0].Trim(), "", parts[1]));
                    }
                    continue;
                }
                rows.Add(new Row(parts[0].Trim(), parts[1], parts[2]));
            }
            return rows;
        }

        private static string[] SplitCsvLine(string line, char delimiter)
        {
            // Minimal CSV splitter supporting quotes
            var list = new List<string>();
            bool inQuotes = false;
            var cur = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                    {
                        cur.Append('\"'); i++; // escaped quote
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == delimiter && !inQuotes)
                {
                    list.Add(cur.ToString()); cur.Clear();
                }
                else
                {
                    cur.Append(c);
                }
            }
            list.Add(cur.ToString());
            return list.ToArray();
        }

        private static void ExplainCommonAdsiErrors(Exception ex)
        {
            // Map a few common HRESULTs/messages to clearer hints
            var msg = ex.Message ?? "";
            uint hr = ex is COMException cex ? (uint)cex.HResult : 0;

            // Common ones:
            // 0x800708C5 NERR_PasswordTooShort / complexity not met (varies)
            // 0x8007202F CONSTRAINT_VIOLATION (password history, complexity, min age, etc.)
            // 0x8007052E Logon failure (bad old password)
            // 0x8007052F Account restriction
            // 0x8009030E No credentials / secure channel required
            // 0x80090322 The target principal name is incorrect (Kerberos/SPN)
            // 0x8007052D Password restriction
            // 0x80090302 No such package (SSPI)
            var hints = new List<string>();

            switch (hr)
            {
                case 0x8007202F: hints.Add("Constraint violation: password may violate history, complexity, or minimum age."); break;
                case 0x800708C5: hints.Add("Password does not meet domain policy (length/complexity/history)."); break;
                case 0x8007052E: hints.Add("Logon failure: the old password is likely incorrect."); break;
                case 0x8009030E: hints.Add("A secure channel is required. Run on a domain-joined machine with Kerberos/LDAPS or provide proper credentials."); break;
                case 0x80090322: hints.Add("Kerberos/SPN issue. Try using the DNS domain, verify time sync, or run under a domain account."); break;
                case 0x8007052D: hints.Add("Password restriction: may violate minimum age or other policy."); break;
            }

            if (msg.Contains("The password does not meet the password policy requirements", StringComparison.OrdinalIgnoreCase))
                hints.Add("Password complexity/length/history failed.");
            if (msg.Contains("server is unwilling to perform", StringComparison.OrdinalIgnoreCase))
                hints.Add("DC refused operation—often policy-related (min age/history) or connection not secure.");

            foreach (var h in hints.Distinct())
                Console.WriteLine($"    Hint: {h}");
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.StartsWith("--"))
                {
                    var key = arg.Substring(2);
                    string value = "";

                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    {
                        value = args[i + 1];
                        i++;
                    }

                    result[key] = value;
                }
            }

            return result;
        }
    }
}
