namespace Lifestyle.SharedKernel.Abstractions;

/// <summary>
/// A single use case. One implementation per feature file, resolved by concrete type in the
/// endpoint lambda — there is deliberately no mediator and no pipeline-behaviour layer
/// (docs/04 §3.3). Cross-cutting concerns are middleware, endpoint filters, or a decorator.
/// </summary>
public interface IHandler<in TRequest, TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken ct);
}
