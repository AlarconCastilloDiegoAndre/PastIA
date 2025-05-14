using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace PastIA.Function  // o el namespace que estés usando
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = new HostBuilder()
                .ConfigureFunctionsWorkerDefaults()
                // Quita estas líneas o comenta el bloque completo
                // .ConfigureServices(services =>
                // {
                //     services.AddApplicationInsightsTelemetryWorkerService();
                //     services.ConfigureFunctionsApplicationInsights();
                // })
                .Build();

            host.Run();
        }
    }
}