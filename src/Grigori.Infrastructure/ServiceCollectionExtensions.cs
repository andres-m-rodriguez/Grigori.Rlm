using Grigori.Infrastructure.LlmClients;
using Grigori.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Grigori.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGrigoriInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure Ollama options
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));

        // Register HttpClient for Ollama
        services.AddHttpClient<ILlmClient, OllamaClient>();

        // Register services
        services.AddSingleton<ChunkingService>();
        services.AddScoped<DeepExplorationService>();

        return services;
    }
}
