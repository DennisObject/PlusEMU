namespace Plus.Core;

internal static class StartupSequence
{
    public static async Task Start(IEnumerable<IStartable> services)
    {
        foreach (var phase in services.GroupBy(service => service.StartOrder).OrderBy(phase => phase.Key)) {
            await Task.WhenAll(phase.Select(StartService));
        }
    }

    private static async Task StartService(IStartable service) => await service.Start();
}
