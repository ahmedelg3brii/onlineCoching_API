using CoachDB.Application.Abstractions.Paging;
using CoachDB.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace CoachDB.Api.Common.Paging;

/// <summary>
/// Paging query string for list endpoints: <c>?cursor=&amp;limit=</c>. Bind with <c>[AsParameters] PageQuery page</c>
/// and convert with <see cref="ToPageRequest"/>, which rejects a bad cursor or limit with a 400.
/// </summary>
public sealed record PageQuery
{
    [FromQuery(Name = "cursor")]
    public string? Cursor { get; init; }

    [FromQuery(Name = "limit")]
    public int? Limit { get; init; }

    public Result<PageRequest> ToPageRequest()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!Application.Abstractions.Paging.Cursor.TryDecode(Cursor, out var after))
            errors["cursor"] = ["invalid_cursor"];

        var limit = Limit ?? PageRequest.DefaultLimit;
        if (limit is < 1 or > PageRequest.MaxLimit)
            errors["limit"] = ["out_of_range"];

        return errors.Count > 0
            ? new ValidationError(errors)
            : new PageRequest(after, limit);
    }
}
