using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace KHost.DataAccess.Contexts;

/// <summary>Sort keys for column types the SQLite provider refuses to ORDER BY (TimeSpan,
/// DateTimeOffset, decimal, ulong), exposed as the TEXT the column already holds.</summary>
internal static class SqliteSortKeys
{
    private static readonly StringTypeMapping _text = new("TEXT", System.Data.DbType.String);

    /// <summary>A TimeSpan column as its stored text. SQLite keeps it in "c" format, which sorts
    /// lexically in duration order below one day; null sorts first, as SQLite orders NULL.</summary>
    public static string TimeSpanText(TimeSpan? value)
        => throw new InvalidOperationException($"{nameof(TimeSpanText)} is only translatable inside an EF query.");

    public static void Register(ModelBuilder modelBuilder)
    {
        // Typing the column as string is the whole trick: the provider checks the ordering's CLR
        // type, and a value conversion instead would change the column and need a table rebuild.
        modelBuilder.HasDbFunction(typeof(SqliteSortKeys).GetMethod(nameof(TimeSpanText))!)
            .HasTranslation(args => new SqlUnaryExpression(
                ExpressionType.Convert, args[0], typeof(string), _text));
    }
}
