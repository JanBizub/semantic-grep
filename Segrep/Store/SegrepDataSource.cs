using Npgsql;
using Segrep.Configuration;

namespace Segrep.Store;

public static class SegrepDataSource
{
    public static NpgsqlDataSource Create(PostgresOptions options)
    {
        var builder = new NpgsqlDataSourceBuilder(options.ConnectionString);
        builder.UseVector();
        return builder.Build();
    }
}
