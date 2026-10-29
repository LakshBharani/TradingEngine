using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TradingEngineServer.Core.Configuration;

namespace TradingEngineServer.Core
{
    public sealed class TradingEngineServerHostBuilder
    {
        public static IHost BuildTradingEngineServer() => Host.CreateDefaultBuilder()
        .UseContentRoot(AppContext.BaseDirectory)
        .ConfigureServices((context, services) =>
        {
            // start with config
            services.AddOptions();
            services.Configure<TradingEngineServerConfiguration>(context.Configuration.GetSection(nameof(TradingEngineServerConfiguration)));

            // add singleton objects
            services.AddSingleton<ITradingEngineServer, TradingEngineServer>();

            // add hosted service
            services.AddHostedService<TradingEngineServer>();
        }).Build();
    }
}
