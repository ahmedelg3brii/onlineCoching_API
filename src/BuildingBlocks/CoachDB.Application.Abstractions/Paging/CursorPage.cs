using System.Buffers.Text;
using System.Text;
using System.Text.Json;

namespace CoachDB.Application.Abstractions.Paging;

/// <summary>The only list shape the API returns: <c>{ items, nextCursor, hasMore }</c>.</summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore)
{
    public static CursorPage<T> Empty { get; } = new([], null, false);

    /// <summary>
    /// Builds a page from rows fetched with <c>Take(request.Limit + 1)</c>: the extra row only tells us
    /// whether another page exists and is not returned.
    /// </summary>
    public static CursorPage<T> From<TRow>(
        IReadOnlyList<TRow> rows, PageRequest request, Func<TRow, T> map, Func<TRow, Cursor> cursorOf)
    {
        var hasMore = rows.Count > request.Limit;
        var pageRows = hasMore ? rows.Take(request.Limit).ToList() : rows;
        var next = hasMore ? cursorOf(pageRows[^1]).Encode() : null;
        return new CursorPage<T>(pageRows.Select(map).ToList(), next, hasMore);
    }
}

/// <summary>Validated paging input for a query. <see cref="Limit"/> is 1–100 (default 20).</summary>
public sealed record PageRequest(Cursor? After, int Limit)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public static PageRequest First(int limit = DefaultLimit) => new(null, Math.Clamp(limit, 1, MaxLimit));
}

/// <summary>
/// Keyset position: the sort key and id of the last row on the previous page. Encoded as opaque
/// base64url JSON so clients never build or parse it.
/// </summary>
public sealed record Cursor(string SortKey, Guid Id)
{
    public string Encode() => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(this));

    public static bool TryDecode(string? value, out Cursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value))
            return true; // no cursor = first page

        try
        {
            cursor = JsonSerializer.Deserialize<Cursor>(Base64Url.DecodeFromChars(value));
            return cursor is not null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }
    }

    public override string ToString() => Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(this));
}
