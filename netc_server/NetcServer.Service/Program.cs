using Microsoft.Extensions.DependencyInjection;
using NetcServer.Core;
using NetcServer.Service;
using NetcShared;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "netc_server";
});

builder.Services.AddSingleton(NetcSettings.Load());
builder.Services.AddSingleton<AgentLink>();
builder.Services.AddSingleton<IAgentBackend, PipeAgentBackend>();
builder.Services.AddHostedService<AgentPipeServer>();
builder.Services.AddHostedService<ServiceRunner>();

var host = builder.Build();
host.Run();
