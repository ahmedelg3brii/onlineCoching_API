using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CoachDB.Infrastructure.Common.Persistence;

/// <summary>Column mappings shared by every module.</summary>
public static class PropertyBuilderExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null, // feature keys are data: store them exactly as written
    };

    /// <summary>
    /// Stores an enum as its snake_case name in a <c>text</c> column (<c>PastDue</c> → <c>'past_due'</c>), the same
    /// spelling the API uses, so SQL, CHECK constraints and JSON agree.
    /// </summary>
    public static PropertyBuilder<TEnum> HasSnakeCaseConversion<TEnum>(this PropertyBuilder<TEnum> builder)
        where TEnum : struct, Enum =>
        builder.HasConversion(SnakeCaseConverter<TEnum>()).HasColumnType("text");

    /// <inheritdoc cref="HasSnakeCaseConversion{TEnum}(PropertyBuilder{TEnum})"/>
    public static PropertyBuilder<TEnum?> HasSnakeCaseConversion<TEnum>(this PropertyBuilder<TEnum?> builder)
        where TEnum : struct, Enum =>
        builder.HasConversion(SnakeCaseConverter<TEnum>()).HasColumnType("text"); // EF never passes null to a converter

    /// <summary>
    /// Column default for rows inserted by hand-written SQL. EF itself always writes the entity's value: the sentinel is
    /// an impossible enum value, so even the CLR default (e.g. the first member) is sent instead of left to the database.
    /// </summary>
    public static PropertyBuilder<TEnum> HasSnakeCaseDefault<TEnum>(this PropertyBuilder<TEnum> builder, TEnum value)
        where TEnum : struct, Enum =>
        builder
            .HasDefaultValueSql($"'{SnakeCase(value.ToString())}'")
            .HasSentinel((TEnum)Enum.ToObject(typeof(TEnum), -1));

    /// <summary>The values the column may hold, for a CHECK constraint: <c>'trial', 'active', ...</c>.</summary>
    public static string SqlValues<TEnum>()
        where TEnum : struct, Enum =>
        string.Join(", ", Enum.GetValues<TEnum>().Select(v => $"'{SnakeCase(v.ToString())}'"));

    /// <summary>
    /// Stores a value object as <c>jsonb</c> with snake_case keys. Compared by its JSON, so replacing the value
    /// with an equal one is not an update, and nested collections compare correctly.
    /// </summary>
    public static PropertyBuilder<T> HasJsonbConversion<T>(this PropertyBuilder<T> builder)
        where T : class
    {
        builder.HasConversion(
            v => JsonSerializer.Serialize(v, Json),
            v => JsonSerializer.Deserialize<T>(v, Json)!,
            new ValueComparer<T>(
                (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
                v => JsonSerializer.Serialize(v, Json).GetHashCode(StringComparison.Ordinal),
                v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Json), Json)!));
        return builder.HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
    }

    private static ValueConverter<TEnum, string> SnakeCaseConverter<TEnum>()
        where TEnum : struct, Enum
    {
        var toDb = Enum.GetValues<TEnum>().ToDictionary(v => v, v => SnakeCase(v.ToString()));
        var fromDb = toDb.ToDictionary(kv => kv.Value, kv => kv.Key);
        return new ValueConverter<TEnum, string>(v => toDb[v], v => fromDb[v]);
    }

    private static string SnakeCase(string name) => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
}
