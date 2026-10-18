using System.Data;
using Dapper;

namespace ManyHands.Data;

/// <summary>
/// Npgsql 10 materialises <c>timestamptz</c> as <see cref="DateTime"/>,
/// <c>bigint</c> as <see cref="long"/>, and reports array columns as
/// <see cref="Array"/> rather than a concrete element type. Dapper picks a
/// record's constructor by comparing each parameter's type to the reader's
/// field type, so records declaring <see cref="DateTimeOffset"/>,
/// <see cref="ulong"/> or <c>string[]</c> find no usable constructor and
/// materialisation throws — a positional record has no property-setter
/// fallback.
///
/// Registering handlers fixes reading: Dapper's constructor matching accepts a
/// parameter type that has a handler, and the handler performs the conversion.
/// The alternative — restating Postgres's storage choices throughout the domain
/// model — is worse.
///
/// Binding a value the other way needs one more step, because Dapper resolves a
/// parameter's DbType from its own built-in map before it ever looks at the
/// handlers. See <see cref="Register"/>.
/// </summary>
internal static class DapperTypeHandlers
{
    internal static void Register()
    {
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
        SqlMapper.AddTypeHandler(new UInt64Handler());
        SqlMapper.AddTypeHandler(new StringArrayHandler());
        SqlMapper.AddTypeHandler(new DateOnlyHandler());

        // Registering a handler is not enough for the parameter half. Dapper
        // consults its built-in DbType map first and only falls through to the
        // handlers for a type it does not recognise, and it does recognise
        // ulong — as DbType.UInt64, which Npgsql rejects outright ("The DbType
        // 'UInt64' isn't supported"). Dropping the entry is what lets
        // UInt64Handler.SetValue do its job and bind a snowflake as the bigint
        // it is stored as. Reads were never affected, which is why this only
        // shows up the first time a Discord id is used in a WHERE clause.
        SqlMapper.RemoveTypeMap(typeof(ulong));
    }

    /// <summary>timestamptz is an instant; UTC with a zero offset is lossless.</summary>
    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) => value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            string s => DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new DataException($"Cannot convert {value.GetType()} to DateTimeOffset.")
        };

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            // Hand Npgsql a UTC DateTime so the parameter is unambiguously
            // timestamptz regardless of the offset the caller happened to hold.
            parameter.Value = value.UtcDateTime;
            parameter.DbType = DbType.DateTime2;
        }
    }

    /// <summary>
    /// Discord snowflake ids are unsigned in Discord.Net but stored as bigint.
    /// Every real snowflake fits in a positive long, so the round trip is safe.
    /// </summary>
    private sealed class UInt64Handler : SqlMapper.TypeHandler<ulong>
    {
        public override ulong Parse(object value) => value switch
        {
            ulong u => u,
            long l => unchecked((ulong)l),
            int i => (ulong)i,
            decimal d => (ulong)d,
            string s => ulong.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new DataException($"Cannot convert {value.GetType()} to ulong.")
        };

        public override void SetValue(IDbDataParameter parameter, ulong value)
        {
            parameter.Value = unchecked((long)value);
            parameter.DbType = DbType.Int64;
        }
    }

    /// <summary>
    /// Postgres `date`. Npgsql reads and writes DateOnly natively, but Dapper
    /// keeps its own DbType lookup and rejects the type outright when binding a
    /// parameter — "cannot be used as a parameter value". A handler is the
    /// supported way to teach it.
    /// </summary>
    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) => value switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            string s => DateOnly.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new DataException($"Cannot convert {value.GetType()} to DateOnly.")
        };

        // No DbType is set: Npgsql infers `date` from the CLR value, and
        // forcing DbType.Date here would make it send a timestamp instead.
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
            => parameter.Value = value;
    }

    /// <summary>
    /// text[] columns (array_agg of activity labels). The runtime value really
    /// is a string[]; it is only GetFieldType() that reports the open
    /// System.Array, which is enough to make Dapper reject the constructor.
    /// </summary>
    private sealed class StringArrayHandler : SqlMapper.TypeHandler<string[]>
    {
        public override string[] Parse(object value) => value switch
        {
            string[] array => array,
            object[] boxed => boxed.Select(v => v?.ToString() ?? string.Empty).ToArray(),
            Array array => array.Cast<object?>().Select(v => v?.ToString() ?? string.Empty).ToArray(),
            _ => throw new DataException($"Cannot convert {value.GetType()} to string[].")
        };

        public override void SetValue(IDbDataParameter parameter, string[]? value)
            => parameter.Value = value ?? (object)DBNull.Value;
    }
}
