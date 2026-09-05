using FluentValidation;
using Lifestyle.Modules.Identity.Contracts;
using Lifestyle.Modules.Identity.Features.Auth;
using Lifestyle.Modules.Identity.Features.Users;
using Lifestyle.Modules.Identity.Internal;
using Lifestyle.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lifestyle.Modules.Identity;

/// <summary>
/// The module's single entry point. The host calls these two methods and knows nothing else about
/// Identity (docs/04 §3.1).
/// </summary>
public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddOptions<IdentityModuleOptions>()
            .BindConfiguration(IdentityModuleOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IIdentityModule, IdentityFacade>();
        services.AddScoped<PermissionResolver>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<ITotpService, TotpService>();

        services.AddValidatorsFromAssembly(typeof(IdentityModule).Assembly, includeInternalTypes: true);
        services.AddHandlersFromAssembly(typeof(IdentityModule).Assembly);

        return services;
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        // The brute-force surface: per-IP throttle on top of the per-account lockout.
        var auth = app.MapGroup("/v1/auth").WithTags("Auth").RequireRateLimiting(RateLimitPolicies.Auth);
        Register.Map(auth);
        Login.Map(auth);
        RefreshSession.Map(auth);
        Logout.Map(auth);
        EnrolTwoFactor.Map(auth);

        var me = app.MapGroup("/v1/me").WithTags("Me").RequireAuthorization();
        GetCurrentUser.Map(me);
        UpdateProfile.Map(me);
        ChangePassword.Map(me);

        return app;
    }
}
