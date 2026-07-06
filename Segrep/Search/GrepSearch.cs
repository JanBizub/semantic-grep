using Npgsql;

namespace Segrep.Search;

public sealed class GrepSearch(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string pattern, int limit = 20, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, file_path, file_hash, chunk_index, chunk_text,
                   similarity(chunk_text, $1)::double precision AS score
            FROM ai_doc_chunk
            WHERE chunk_text ILIKE '%' || $1 || '%'
            ORDER BY score DESC
            LIMIT $2
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(pattern);
        command.Parameters.AddWithValue(limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<SearchResult>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SearchResult(
                Id: reader.GetInt64(0),
                FilePath: reader.GetString(1),
                FileHash: reader.GetString(2),
                ChunkIndex: reader.GetInt32(3),
                ChunkText: reader.GetString(4),
                Score: reader.GetDouble(5)
            ));
        }

        return results;
    }
}
