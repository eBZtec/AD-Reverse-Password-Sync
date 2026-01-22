Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureServices(services =>
    {
        services.AddHostedService<PipeServerWorker>();
    })
    .Build()
    .Run();