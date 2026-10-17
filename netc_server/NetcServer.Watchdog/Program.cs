using Microsoft.Extensions.Hosting.WindowsServices;
using NetcServer.Watchdog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "netc_watchdog";
});

builder.Services.AddHostedService<WatchdogWorker>();
builder.Services.AddHostedService<MachinePersonalizationWorker>();

builder.Build().Run();
