
using Examples;
using Examples.Client;
using Examples.Gateway;
using Examples.Services;
using Light.Message;
using Serilog;
using Serilog.Core;
using Serilog.Extensions.Logging;

namespace Light.Transmit
{



    public class Program
    {


        private static readonly Logger logger = InitSerilog();

        //private static readonly GCEventListener gCEventListener = new GCEventListener();
        private static Logger InitSerilog()
        {
            var configuration = new LoggerConfiguration();
            configuration.MinimumLevel.Is(Serilog.Events.LogEventLevel.Information);
            configuration.MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning);
            configuration.WriteTo.Async(configure =>
            {
                configure.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss fff} [{Level:u4}] {Message:lj}{NewLine}{Exception}");//  [{SourceContext}]
            });
            configuration.Enrich.FromLogContext();
            var _logger = configuration.CreateLogger();
            ILoggerFactory loggerFactory = new SerilogLoggerFactory(_logger);
            LoggerProvider.Initialize(loggerFactory);
            return _logger;
        }


        public static async Task Main(string[] args)
        {
            // 动态调整 内存池容量
            MFactory<ClientMessage>.SetPoolMaxCapacity(500000);
            // 动态调整 禁用内存池
            MFactory<GatewayPingMessage>.SetPoolMaxCapacity(500000);

            //var pts = new HighPerformanceTcpServer();

            //pts.Start("0.0.0.0", 22222);


            //var tcc = new TCPClient();
            //tcc.SetAdapter(new NoneClientHandlerAdapter());
            //await tcc.ConnectAsync("127.0.0.1", 22222, CancellationToken.None);
            //var bytes = new Byte[] { 1, 2, 3, 4, 5 };
            //await tcc.Session.WriteAsync(bytes.AsMemory());



            //


            var host = CreateHostBuilder(args).Build();
            await host.RunAsync();
            host.Dispose();
            Console.ReadLine();
        }


        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                 .UseSerilog(logger)
                 .ConfigureServices(ConfigureServices);
        }


        private static void ConfigureServices(IServiceCollection services)
        {
            var ipBlock = new IPBlacklistTrie();
            ipBlock.Add("127.0.0.1");
            ipBlock.Add("192.168.1.1/24");

            var appOptions = new ApplicationOptions();

            services.AddSingleton<ApplicationOptions>(appOptions);

            services.AddSingleton<IPBlacklistTrie>(ipBlock);



            services.AddTimeService();
            //services.AddSingleton<TestService>();
            //services.AddHostedService(provider => provider.GetRequiredService<TestService>());

            services.AddSingleton<ClientMessageProcessService>();
            services.AddHostedService(provider => provider.GetRequiredService<ClientMessageProcessService>());

            services.AddSingleton<GatewayMessageProcessService>();
            services.AddHostedService(provider => provider.GetRequiredService<GatewayMessageProcessService>());



            if (Environment.CommandLine.Contains("server"))
            {
                services.AddSingleton<GatewayServerService>();
                services.AddHostedService(provider => provider.GetRequiredService<GatewayServerService>());
            }

            if (Environment.CommandLine.Contains("client"))
            {
                services.AddSingleton<MessageClientService>();
                services.AddHostedService(provider => provider.GetRequiredService<MessageClientService>());
            }
        }

    }
}
