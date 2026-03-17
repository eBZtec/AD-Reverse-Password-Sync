using System.IO.Pipes;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

public class PipeServer
{
    private const string PipeName = "EbzPassFilter";
    private const int MaxMessageBytes = 64 * 1024;

    public PipeServer(){}
    
    private static NamedPipeServerStream CreateSecureServer()
    {
        var ps = new PipeSecurity();
        ps.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        ps.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        var server = NamedPipeServerStreamAcl.Create(
            pipeName: PipeName,
            direction: PipeDirection.InOut,
            maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous,
            inBufferSize: 1024,
            outBufferSize: 4,
            pipeSecurity: ps
        );

        return server;
    }

    public async Task ServerFlow(CancellationToken token)
    {
        LogManager.Log($"[PipeServer] ServerFlow: Waiting for connections... {Assembly.GetEntryAssembly().GetName().Version.ToString()}");

        var clientTasks = new List<Task>();

        try
        {
            while (!token.IsCancellationRequested)
            {
                var pipeServer = CreateSecureServer();

                try
                {
                    await pipeServer.WaitForConnectionAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    pipeServer.Dispose();
                    break;
                }

                LogManager.Log("[PipeServer] ServerFlow: Client connected!");

                clientTasks.Add(HandleClientAsync(pipeServer, token));

                clientTasks.RemoveAll(t => t.IsCompleted);
            }
        }
        finally
        {
            try { await Task.WhenAll(clientTasks); } catch {}
        }
    }

    private static async Task HandleClientAsync(NamedPipeServerStream pipeServer, CancellationToken token)
    {
        DateTime start = DateTime.Now;

        try
        {
            using (pipeServer)
            using (var reader = new BinaryReader(pipeServer, Encoding.Unicode, leaveOpen: true))
            using (var writer = new BinaryWriter(pipeServer, Encoding.Unicode, leaveOpen: true))
            {
                int length;
                try
                {
                    length = reader.ReadInt32();
                }
                catch (EndOfStreamException)
                {
                    LogManager.Log("[PipeServer] HandleClientAsync: Client disconnected before sending length.");
                    return;
                }

                if (length < 1 || length > MaxMessageBytes)
                {
                    writer.Write(0);
                    writer.Flush();
                    LogManager.Log($"[PipeServer] HandleClientAsync: Invalid length={length}.");
                    return;
                }

                byte[] buffer = reader.ReadBytes(length);
                if (buffer.Length != length)
                {
                    writer.Write(0);
                    writer.Flush();
                    LogManager.Log("[PipeServer] HandleClientAsync: Incomplete message received.");
                    return;
                }

                string encryptedMessage = Encoding.Unicode.GetString(buffer);

                DPAPI.DecryptBase64(encryptedMessage, out string? decryptedMessage);

                if (!JsonHelper.Deserialize<Profile>(decryptedMessage, out var profile) || profile is null)
                {
                    writer.Write(0);
                    writer.Flush();
                    LogManager.Log("[PipeServer] HandleClientAsync: Failed deserialization. Password or username may contain invalid characters such as \" or \\");
                    return;
                }

                if (string.IsNullOrEmpty(profile.pass))
                {
                    writer.Write(0);
                    writer.Flush();
                    LogManager.Log("[PipeServer] HandleClientAsync: Password is NULL or Empty.");
                    return;
                }

                if (RegistryReader.AllowAllChanges()) // BYPASS MIDPOINT
                {
                    writer.Write(1);
                    writer.Flush();
                    LogManager.Log($"[PipeServer] HandleClientAsync: Midpoint update bypassed using Registry FLAG, user: {profile.user} updated on AD.");
                    return;
                }

                if (RegistryReader.GetAdminBypass() && profile.set.Equals(1)) // ADMIN BYPASS MIDPOINT
                {
                    writer.Write(1);
                    writer.Flush();
                    LogManager.Log($"[PipeServer] HandleClientAsync: Midpoint update bypassed by ADMIN, user: {profile.user} updated on AD.");
                    return;
                }

                (string? oidResult, string? nameResult, string? statusCode) = await MidPointApi.SearchUser(profile.user); // MIDPOINT API

                if (statusCode == "no_oid")
                {
                    writer.Write(1);
                    writer.Flush();
                    LogManager.Log($"[PipeServer] HandleClientAsync: no_oid -> skipping MidPoint change for user: {profile.user} (AD already updated).");
                    return;
                }

                if (statusCode == "no_name")
                {
                    writer.Write(0);
                    writer.Flush();
                    LogManager.Log($"[PipeServer] HandleClientAsync: no_name -> could not authenticate in Midpoint for user: {profile.user}");
                    return;
                }

                if (statusCode == "failed_search")
                {
                    writer.Write(0);
                    writer.Flush();
                    LogManager.Log($"[PipeServer] HandleClientAsync: failed_search for user: {profile.user}.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(oidResult))
                {
                    writer.Write(0);
                    writer.Flush();
                    LogManager.Log($"[PipeServer] HandleClientAsync: Empty oidResult with non-error status for user: {profile.user}");
                    return;
                }

                if(await MidPointApi.AuthenticateUser(nameResult ?? profile.user, profile.pass))
                {
                    writer.Write(1);
                    writer.Flush();
                    LogManager.Log($"[PipeServer] HandleClientAsync: The user {profile.user} ({nameResult} in midPoint) already have this password, returning SUCCESS");
                    return;
                }

                bool patched = await MidPointApi.PatchChange(oidResult, profile.pass);
                writer.Write(patched ? 1 : 0);
                writer.Flush();

                if (patched)
                    LogManager.Log($"[PipeServer] HandleClientAsync: Password changed on MidPoint for user: {profile.user}(oid={oidResult})");
                else
                    LogManager.Log($"[PipeServer] HandleClientAsync: PatchChange failed for user: {profile.user}(oid={oidResult})");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogManager.Log($"[PipeServer] HandleClientAsync -> Exception: {ex}");
        }
        finally
        {
            int elapsedMs = (int)(DateTime.Now - start).TotalMilliseconds;
            LogManager.Log($"[PipeServer] HandleClientAsync -> Elapsed time in ms during change: {elapsedMs}");
        }
    }
}