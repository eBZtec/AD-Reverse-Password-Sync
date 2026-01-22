public sealed class PipeServerWorker : BackgroundService
{
    public PipeServerWorker(){}

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var server = new PipeServer();
        
        try
        {
            await server.ServerFlow(stoppingToken);
        }
        catch (OperationCanceledException)
        {}
        catch (Exception ex)
        {
            LogManager.Log($"[PipeServerWorker] ExecuteAsync: Pipe server crashed -> {ex}");
        }
        finally
        {
            LogManager.Log($"[PipeServerWorker] ExecuteAsync: Pipe server stopping");
        }
    }
}
