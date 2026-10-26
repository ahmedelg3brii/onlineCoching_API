using CoachDB.Api.Common.Paging;
using CoachDB.Application.Abstractions.Paging;
using CoachDB.SharedKernel.Results;

namespace CoachDB.BuildingBlocks.UnitTests.Paging;

public sealed class CursorTests
{
    [Fact]
    public void Encode_then_decode_round_trips()
    {
        var cursor = new Cursor("2026-10-26T08:00:00Z", Guid.CreateVersion7());

        Cursor.TryDecode(cursor.Encode(), out var decoded).ShouldBeTrue();
        decoded.ShouldBe(cursor);
    }

    [Fact]
    public void Encoded_cursor_is_url_safe()
    {
        var encoded = new Cursor("a/b+c?d=e", Guid.CreateVersion7()).Encode();

        encoded.ShouldNotContain("+");
        encoded.ShouldNotContain("/");
        encoded.ShouldNotContain("=");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Missing_cursor_means_first_page(string? value)
    {
        Cursor.TryDecode(value, out var decoded).ShouldBeTrue();
        decoded.ShouldBeNull();
    }

    [Theory]
    [InlineData("!!!not-base64!!!")]
    [InlineData("bm90LWpzb24")] // base64url of "not-json"
    [InlineData("bnVsbA")]      // base64url of "null"
    public void Invalid_cursor_is_rejected(string value)
    {
        Cursor.TryDecode(value, out var decoded).ShouldBeFalse();
        decoded.ShouldBeNull();
    }
}

public sealed class CursorPageTests
{
    private sealed record Row(Guid Id, int N);

    private static List<Row> Rows(int count) =>
        Enumerable.Range(1, count).Select(n => new Row(Guid.CreateVersion7(), n)).ToList();

    private static CursorPage<int> Page(List<Row> rows, int limit) =>
        CursorPage<int>.From(rows, PageRequest.First(limit), r => r.N, r => new Cursor(r.N.ToString(System.Globalization.CultureInfo.InvariantCulture), r.Id));

    [Fact]
    public void Extra_row_means_more_pages_and_is_not_returned()
    {
        var rows = Rows(4); // fetched limit + 1
        var page = Page(rows, limit: 3);

        page.Items.ShouldBe([1, 2, 3]);
        page.HasMore.ShouldBeTrue();
        page.NextCursor.ShouldNotBeNull();
        Cursor.TryDecode(page.NextCursor, out var next).ShouldBeTrue();
        next.ShouldBe(new Cursor("3", rows[2].Id)); // last returned row, not the extra one
    }

    [Fact]
    public void Exactly_limit_rows_is_the_last_page()
    {
        var page = Page(Rows(3), limit: 3);

        page.Items.Count.ShouldBe(3);
        page.HasMore.ShouldBeFalse();
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public void No_rows_is_an_empty_last_page()
    {
        var page = Page([], limit: 20);

        page.Items.ShouldBeEmpty();
        page.HasMore.ShouldBeFalse();
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public void First_clamps_the_limit()
    {
        PageRequest.First(0).Limit.ShouldBe(1);
        PageRequest.First(1000).Limit.ShouldBe(PageRequest.MaxLimit);
        PageRequest.First().Limit.ShouldBe(PageRequest.DefaultLimit);
    }
}

public sealed class PageQueryTests
{
    [Fact]
    public void Defaults_to_first_page_of_twenty()
    {
        var result = new PageQuery().ToPageRequest();

        result.IsSuccess.ShouldBeTrue();
        result.Value.After.ShouldBeNull();
        result.Value.Limit.ShouldBe(PageRequest.DefaultLimit);
    }

    [Fact]
    public void Valid_cursor_and_limit_are_accepted()
    {
        var cursor = new Cursor("k", Guid.CreateVersion7());

        var result = new PageQuery { Cursor = cursor.Encode(), Limit = 50 }.ToPageRequest();

        result.IsSuccess.ShouldBeTrue();
        result.Value.After.ShouldBe(cursor);
        result.Value.Limit.ShouldBe(50);
    }

    [Fact]
    public void Invalid_cursor_is_a_validation_error()
    {
        var result = new PageQuery { Cursor = "garbage!!" }.ToPageRequest();

        var error = result.Error.ShouldBeOfType<ValidationError>();
        error.Errors["cursor"].ShouldBe(["invalid_cursor"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Out_of_range_limit_is_a_validation_error(int limit)
    {
        var result = new PageQuery { Limit = limit }.ToPageRequest();

        var error = result.Error.ShouldBeOfType<ValidationError>();
        error.Errors["limit"].ShouldBe(["out_of_range"]);
    }

    [Fact]
    public void Both_errors_are_reported_together()
    {
        var error = new PageQuery { Cursor = "garbage!!", Limit = 500 }.ToPageRequest().Error.ShouldBeOfType<ValidationError>();

        error.Errors.Keys.ShouldBe(["cursor", "limit"], ignoreOrder: true);
    }
}
