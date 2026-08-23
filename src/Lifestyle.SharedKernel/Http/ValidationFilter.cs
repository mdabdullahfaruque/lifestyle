using FluentValidation;
using Lifestyle.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Lifestyle.SharedKernel.Http;

/// <summary>
/// Runs the FluentValidation validator for <typeparamref name="TRequest"/> before the handler.
/// Applied per endpoint with <c>.Validate&lt;T&gt;()</c> rather than globally, so it is visible at
/// the call site which endpoints validate what.
/// </summary>
public sealed class ValidationFilter<TRequest> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validator = context.HttpContext.RequestServices.GetService<IValidator<TRequest>>();
        if (validator is null) return await next(context);

        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null) return await next(context);

        var validation = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
        if (validation.IsValid) return await next(context);

        var fieldErrors = validation.Errors
            .GroupBy(e => ToCamelCase(e.PropertyName), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray(), StringComparer.Ordinal);

        return ResultExtensions.Problem(Error.Validation(fieldErrors));
    }

    // FluentValidation reports PascalCase property paths; the wire format is camelCase (FRD §19.1).
    private static string ToCamelCase(string propertyPath) =>
        string.Join('.', propertyPath.Split('.').Select(segment =>
            segment.Length == 0 || char.IsLower(segment[0])
                ? segment
                : char.ToLowerInvariant(segment[0]) + segment[1..]));
}

public static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder Validate<TRequest>(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<ValidationFilter<TRequest>>()
            .ProducesValidationProblem();
}
