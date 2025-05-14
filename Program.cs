using Microsoft.Extensions.Hosting;

namespace PastIA.Function
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = new HostBuilder()
                .ConfigureFunctionsWorkerDefaults()
                // Elimina o comenta estas líneas que causan el error
                // .ConfigureServices(services =>
                // {
                //    services.AddApplicationInsightsTelemetryWorkerService();
                //    services.ConfigureFunctionsApplicationInsights();
                // })
                .Build();

            host.Run();
        }
    }
}