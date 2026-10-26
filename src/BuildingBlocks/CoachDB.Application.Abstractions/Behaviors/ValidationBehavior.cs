using CoachDB.Application.Abstractions.Messaging;
using CoachDB.SharedKernel.Results;
using FluentValidation;
using Mediator;

namespace CoachDB.Application.Abstractions.Behaviors;

/// <summary>
/// Pipeline step 2: runs every FluentValidation validator for the message. Failures become a
/// <see cref="ValidationError"/> (HTTP 400) instead of an exception. Validator messages are error codes,
/// e.g. <c>RuleFor(x => x.Name).NotEmpty().WithMessage("required")</c>.
/// </summary>
public sealed class ValidationBehavior<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!ResultFailure<TResponse>.IsSupported)
            return await next(message, cancellationToken);

        var context = new ValidationContext<TMessage>(message);
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
            return await next(message, cancellationToken);

        var errors = failures
            .GroupBy(f => ToCamelPath(f.PropertyName), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray(), StringComparer.Ordinal);

        return ResultFailure<TResponse>.Create(new ValidationError(errors));
    }

    // "Price.Amount" -> "price.amount", "Items[0].FoodId" -> "items[0].foodId", matching the JSON body.
    private static string ToCamelPath(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(part =>
            part.Length == 0 ? part : char.ToLowerInvariant(part[0]) + part[1..]));
}
