using Grigori.Infrastructure.LlmClients;
using Grigori.Infrastructure.Orchestration;
using Grigori.Infrastructure.Sandbox;
using Grigori.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Grigori.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGrigoriInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure options
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));
        services.Configure<SandboxOptions>(configuration.GetSection(SandboxOptions.SectionName));

        // Register HttpClients
        services.AddHttpClient<ILlmClient, OllamaClient>();
        services.AddHttpClient<ISandboxService, PythonSandboxService>();

        // Register services
        services.AddSingleton<ChunkingService>();
        services.AddScoped<DeepExplorationService>();
        services.AddScoped<RlmOrchestrator>();

        return services;
    }
}
