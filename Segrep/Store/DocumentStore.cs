using Npgsql;
using Segrep.Documents;

namespace Segrep.Store;

public sealed class DocumentStore(NpgsqlDataSource dataSource)
{
    public async Task<(Guid Id, bool Replaced)> AddAsync(ParsedDocument document, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var deleteCmd = connection.CreateCommand();
        deleteCmd.Transaction = transaction;
        deleteCmd.CommandText = "DELETE FROM ai_document WHERE lower(name) = lower($1)";
        deleteCmd.Parameters.AddWithValue(document.Name);
        var replaced = await deleteCmd.ExecuteNonQueryAsync(cancellationToken) > 0;

        await using var insertCmd = connection.CreateCommand();
        insertCmd.Transaction = transaction;
        insertCmd.CommandText = "INSERT INTO ai_document (name, preamble) VALUES ($1, $2) RETURNING id";
        insertCmd.Parameters.AddWithValue(document.Name);
        insertCmd.Parameters.AddWithValue(document.Preamble);
        var documentId = (Guid)(await insertCmd.ExecuteScalarAsync(cancellationToken))!;

        foreach (var section in document.Sections)
        {
            await InsertSectionAsync(connection, transaction, documentId, parentId: null, section, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (documentId, replaced);
    }

    private static async Task InsertSectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid documentId,
        Guid? parentId,
        ParsedSection section,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO ai_document_section (document_id, parent_id, title, content, level, position)
            VALUES ($1, $2, $3, $4, $5, $6)
            RETURNING id
            """;
        cmd.Parameters.AddWithValue(documentId);
        cmd.Parameters.AddWithValue((object?)parentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(section.Title);
        cmd.Parameters.AddWithValue(section.Content);
        cmd.Parameters.AddWithValue(section.Level);
        cmd.Parameters.AddWithValue(section.Position);
        var sectionId = (Guid)(await cmd.ExecuteScalarAsync(cancellationToken))!;

        foreach (var child in section.Children)
        {
            await InsertSectionAsync(connection, transaction, documentId, sectionId, child, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<(Guid Id, string Name, DateTimeOffset CreatedAt)>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, created_at FROM ai_document ORDER BY created_at";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var results = new List<(Guid, string, DateTimeOffset)>();
        while (await reader.ReadAsync(cancellationToken))
            results.Add((reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2)));

        return results;
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM ai_document WHERE id = $1";
        cmd.Parameters.AddWithValue(id);
        return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<int> ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM ai_document";
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*)::int FROM ai_document";
        return (int)(await cmd.ExecuteScalarAsync(cancellationToken))!;
    }
}
