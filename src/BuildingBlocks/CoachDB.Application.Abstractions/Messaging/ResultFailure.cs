using System.Reflection;
using CoachDB.SharedKernel.Results;

namespace CoachDB.Application.Abstractions.Messaging;

/// <summary>
/// Lets an open-generic pipeline behavior return a failed <c>Result</c> / <c>Result&lt;T&gt;</c>
/// without knowing <typeparamref name="TResponse"/>. The factory is built once per response type.
/// </summary>
internal static class ResultFailure<TResponse>
{
    private static readonly Func<Error, TResponse>? Factory = Build();

    public static bool IsSupported => Factory is not null;

    public static TResponse Create(Error error) =>
        Factory is not null
            ? Factory(error)
            : throw new InvalidOperationException($"{typeof(TResponse).Name} is not a Result type.");

    private static Func<Error, TResponse>? Build()
    {
        var type = typeof(TResponse);

        if (type == typeof(Result))
            return error => (TResponse)(object)Result.Failure(error);

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var method = typeof(Result)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m is { Name: nameof(Result.Failure), IsGenericMethodDefinition: true })
                .MakeGenericMethod(type.GetGenericArguments()[0]);

            return error => (TResponse)method.Invoke(null, [error])!;
        }

        return null;
    }
}
