using LlmModelProxy.Core.Interfaces;
using LlmModelProxy.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace LlmModelProxy.Infrastructure;

/// <summary>
/// Extension methods for registering infrastructure-layer services.
/// </summary>
public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Registers the <see cref="ProxyAuditLogger"/> as a singleton
    /// <see cref="IProxyAuditLogger"/> implementation.
    /// </summary>
    public static IServiceCollection AddProxyInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IProxyAuditLogger, ProxyAuditLogger>();
        return services;
    }
}
