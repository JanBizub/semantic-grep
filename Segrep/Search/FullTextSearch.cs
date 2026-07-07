using Npgsql;

namespace Segrep.Search;

public sealed class FullTextSearch(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, file_path, file_hash, chunk_index, chunk_text,
                   ts_rank(content_tsv, websearch_to_tsquery('english', $1))::double precision AS score,
                   page_start, page_end
            FROM ai_doc_chunk
            WHERE content_tsv @@ websearch_to_tsquery('english', $1)
            ORDER BY score DESC
            LIMIT $2
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(query);
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
                Score: reader.GetDouble(5),
                PageStart: reader.IsDBNull(6) ? null : reader.GetInt32(6),
                PageEnd: reader.IsDBNull(7) ? null : reader.GetInt32(7)
            ));
        }

        return results;
    }
}
