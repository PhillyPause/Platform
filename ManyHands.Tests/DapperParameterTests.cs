// LookupDbType is marked internal-use, but consulting it is precisely the
// point: these tests assert WHICH path Dapper takes for a parameter type.
#pragma warning disable CS0618
using System.Data;
using Dapper;
using FluentAssertions;
using ManyHands.Data;

namespace ManyHands.Tests;

/// <summary>
/// The PARAMETER half of the Dapper type story. Registering a handler is not
/// enough: Dapper consults its built-in DbType map first and only falls through
/// to handlers for types it does not recognise — and it recognises <c>ulong</c>
/// as DbType.UInt64, which Npgsql rejects outright. These tests assert the
/// handler is actually the one consulted, not merely that one is registered;
/// an earlier "is a handler registered" assertion passed while every ulong
/// WHERE clause in the product threw.
/// </summary>
public sealed class DapperParameterTests
{
    public DapperParameterTests() => DapperConfiguration.Configure();

    [Fact]
    public void A_ulong_parameter_reaches_the_handler_not_the_builtin_map()
    {
        // Without RemoveTypeMap(typeof(ulong)) this returns DbType.UInt64 with
        // no handler, and Npgsql refuses the command at execution time.
        SqlMapper.LookupDbType(typeof(ulong), "discordId", false, out var handler);

        handler.Should().NotBeNull(
            "the built-in UInt64 mapping must be removed so the handler binds snowflakes as bigint");
    }

    [Fact]
    public void A_DateOnly_parameter_reaches_the_handler()
    {
        SqlMapper.LookupDbType(typeof(DateOnly), "workedOn", false, out var handler);

        handler.Should().NotBeNull("DateOnly cannot bind without its handler");
    }

    [Fact]
    public void A_DateTimeOffset_parameter_rides_the_builtin_map_and_that_is_fine()
    {
        // The asymmetry worth pinning: DateTimeOffset needs its handler for
        // READS (Npgsql returns timestamptz as DateTime), but on the parameter
        // path Dapper's built-in DbType.DateTimeOffset entry wins — and unlike
        // DbType.UInt64, Npgsql accepts it. So no RemoveTypeMap here, and the
        // handler is legitimately not consulted for parameters.
        var dbType = SqlMapper.LookupDbType(typeof(DateTimeOffset), "startsAt", false, out var handler);

        handler.Should().BeNull("the built-in map covers the parameter path");
        dbType.Should().Be(DbType.DateTimeOffset);
    }

#nullable disable
    private sealed class StubDbParameter : IDbDataParameter
    {
        public DbType DbType { get; set; }
        public ParameterDirection Direction { get; set; }
        public bool IsNullable => true;
        public string ParameterName { get; set; } = string.Empty;
        public string SourceColumn { get; set; } = string.Empty;
        public DataRowVersion SourceVersion { get; set; }
        public object Value { get; set; }
        public byte Precision { get; set; }
        public byte Scale { get; set; }
        public int Size { get; set; }
    }
#nullable restore
}
