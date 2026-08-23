using System.Reflection;
using Lifestyle.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lifestyle.SharedKernel.Http;

public static class HandlerRegistration
{
    /// <summary>
    /// Registers every <see cref="IHandler{TRequest,TResponse}"/> in the assembly by its concrete
    /// type, which is how endpoints resolve them. Scoped, because handlers take the DbContext.
    /// </summary>
    public static IServiceCollection AddHandlersFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .Where(t => Array.Exists(t.GetInterfaces(),
                i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHandler<,>)));

        foreach (var type in handlerTypes)
            services.AddScoped(type);

        return services;
    }
}
