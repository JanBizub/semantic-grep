using System.Reflection;
using Npgsql;

namespace Segrep.Store;

public static class SchemaMigrator
{
    private static readonly string Schema = LoadSchema();

    public static async Task EnsureCreatedAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = Schema;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string LoadSchema()
    {
        var assembly = typeof(SchemaMigrator).Assembly;
        const string resourceName = "Segrep.Store.Schema.sql";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
