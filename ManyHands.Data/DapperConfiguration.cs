using Dapper;

namespace ManyHands.Data;

public static class DapperConfiguration
{
    private static bool _configured;
    private static readonly Lock Gate = new();

    /// <summary>
    /// Column-name mapping plus the type handlers that let records declare
    /// DateTimeOffset and ulong against timestamptz and bigint columns.
    ///
    /// Worth calling explicitly because the failure modes are quiet or
    /// confusing: without name mapping a query returns records whose properties
    /// are all default; without the handlers materialisation throws about a
    /// missing constructor. Called from the DI registration and from the test
    /// fixture, so both the hosted app and tests that build a repository
    /// directly are covered. Idempotent.
    /// </summary>
    public static void Configure()
    {
        if (_configured) return;

        lock (Gate)
        {
            if (_configured) return;

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            DapperTypeHandlers.Register();

            _configured = true;
        }
    }
}
