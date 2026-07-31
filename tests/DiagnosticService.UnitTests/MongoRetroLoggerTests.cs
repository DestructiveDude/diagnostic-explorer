using AwesomeAssertions;
using Diagnostic.Service.Common;
using Diagnostic.Service.Transport;
using MongoDB.Bson;
using Xunit;

namespace DiagnosticService.UnitTests;

/// <summary>
///     Covers the Mongo retro backend's input-validation rules, all of which return before any
///     driver call (the ctor only builds a <c>Lazy&lt;MongoClient&gt;</c>), so no Mongo server is
///     needed. <c>MongoRetroLogger</c> is the default retro backend, and its
///     <c>ValidateFilterPattern</c> is a separate private copy from the Log Analytics one — the
///     LogAnalyticsRetroLoggerTests coverage does not transfer. (DE-8)
/// </summary>
public class MongoRetroLoggerTests
{
    // A syntactically valid connection string with a short server-selection timeout: no server
    // is listening, so any code path that accidentally reaches the driver fails fast instead of
    // hanging for the default 30s selection timeout.
    private const string DummyConnectionString =
        "mongodb://127.0.0.1:27017/?serverSelectionTimeoutMS=500";

    private static MongoRetroLogger CreateLogger()
    {
        return new MongoRetroLogger(DummyConnectionString);
    }

    private static RetroQuery BaseQuery()
    {
        return new RetroQuery
        {
            SearchId = 1,
            MaxRecords = 100,
            MinLevel = 2,
            StartDate = new DateTime(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc),
        };
    }

    private static void SetField(RetroQuery query, string field, string pattern)
    {
        switch (field)
        {
            case "Machine":
                query.Machine = pattern;
                break;
            case "User":
                query.User = pattern;
                break;
            case "Process":
                query.Process = pattern;
                break;
            case "Message":
                query.Message = pattern;
                break;
        }
    }

    public static IEnumerable<object?[]> DeleteCasesReturningZero =>
        new object?[][]
        {
            new object?[] { null },
            new object?[] { Array.Empty<string>() },
            new object?[] { new[] { "not-an-objectid", "also-not-valid" } },
        };

    /// <summary>
    ///     A null or empty id list is a no-op returning 0, and an all-malformed id list must
    ///     return 0 rather than letting a FormatException escape the batch — a single malformed
    ///     id previously threw an unhandled FormatException out of the whole delete. (DE-8)
    /// </summary>
    [Theory]
    [MemberData(nameof(DeleteCasesReturningZero))]
    public async Task Delete_WithNullEmptyOrAllMalformedIds_ReturnsZero(string[]? idList)
    {
        MongoRetroLogger logger = CreateLogger();

        var deleted = await logger.Delete(idList!);

        deleted.Should().Be(0);
    }

    /// <summary>
    ///     A delete batch over the 10,000-id limit must be rejected with ArgumentException — it
    ///     is refused outright, not capped — before any driver call. (DE-8)
    /// </summary>
    [Fact]
    public async Task Delete_WithMoreThan10000Ids_ThrowsArgumentException()
    {
        MongoRetroLogger logger = CreateLogger();
        var idList = Enumerable
            .Range(0, 10_001)
            .Select(_ => ObjectId.GenerateNewId().ToString())
            .ToArray();

        Func<Task> act = async () => await logger.Delete(idList);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*exceeds the limit*");
    }

    /// <summary>
    ///     A pattern that does not compile as a .NET regex must throw ArgumentException naming
    ///     the offending field, on the first MoveNextAsync — the async-iterator body validates
    ///     before FindAsync, so the failure surfaces before any driver call. (DE-8)
    /// </summary>
    [Theory]
    [InlineData("Machine")]
    [InlineData("User")]
    [InlineData("Process")]
    [InlineData("Message")]
    public async Task GetMessages_WithInvalidRegex_ThrowsOnFirstMove(string field)
    {
        MongoRetroLogger logger = CreateLogger();
        var query = BaseQuery();
        SetField(query, field, "["); // not a valid regex

        await using var enumerator = logger
            .GetMessages(query, TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Func<Task> act = async () => await enumerator.MoveNextAsync();

        await act.Should().ThrowAsync<ArgumentException>().WithMessage($"*{field}*");
    }

    /// <summary>
    ///     A pattern over the 256-character limit must be rejected before any driver call. (DE-8)
    /// </summary>
    [Fact]
    public async Task GetMessages_WithOverlongPattern_ThrowsOnFirstMove()
    {
        MongoRetroLogger logger = CreateLogger();
        var query = BaseQuery();
        query.Machine = new string('a', 257);

        await using var enumerator = logger
            .GetMessages(query, TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Func<Task> act = async () => await enumerator.MoveNextAsync();

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Machine*");
    }

    // The Mongo copy runs patterns as server-side $regex (PCRE), so constructs the RE2-based
    // Log Analytics copy must reject — lookahead, backreferences — are legitimate here and must
    // NOT be over-rejected. Validation passing means the first MoveNextAsync proceeds past
    // ValidateFilterPattern to FindAsync; with no server listening that fails with a driver
    // exception (or succeeds if a local Mongo happens to be up) — anything except an
    // ArgumentException proves the pattern was not rejected. (DE-8)
    [Theory]
    [InlineData("srv\\d+")] // ordinary regex
    [InlineData("a(?=b)")] // lookahead — PCRE supports it
    [InlineData("(a)\\1")] // numeric backreference — PCRE supports it
    [InlineData("(?<g>a)\\k<g>")] // named backreference — PCRE supports it
    public async Task GetMessages_WithValidPattern_IsNotOverRejected(string pattern)
    {
        MongoRetroLogger logger = CreateLogger();
        var query = BaseQuery();
        query.Message = pattern;

        await using var enumerator = logger
            .GetMessages(query, TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var exception = await Record.ExceptionAsync(async () => await enumerator.MoveNextAsync());

        exception.Should().NotBeOfType<ArgumentException>();
    }

    /// <summary>
    ///     The 256-character boundary itself must be accepted — only lengths over the limit are
    ///     rejected. (DE-8)
    /// </summary>
    [Fact]
    public async Task GetMessages_WithExactlyMaxLengthPattern_IsNotOverRejected()
    {
        MongoRetroLogger logger = CreateLogger();
        var query = BaseQuery();
        query.Message = new string('a', 256);

        await using var enumerator = logger
            .GetMessages(query, TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var exception = await Record.ExceptionAsync(async () => await enumerator.MoveNextAsync());

        exception.Should().NotBeOfType<ArgumentException>();
    }
}
