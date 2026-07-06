using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;
using Segrep.Configuration;
using Segrep.Embeddings;

namespace Segrep.Search;

public sealed class SemanticSearch(
    EmbeddingService embeddingService,
    IOptions<EmbeddingModelOptions> modelOptions,
    NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        var opts = modelOptions.Value;
        var embeddings = await embeddingService.EmbedAsync([query], cancellationToken);
        var vector = new Vector(embeddings[0].Vector.ToArray());

        const string sql = """
            SELECT id, file_path, file_hash, chunk_index, chunk_text,
                   (1 - (embedding <=> $1))::double precision AS score
            FROM ai_doc_chunk
            WHERE model_name = $2 AND dim = $3
            ORDER BY embedding <=> $1
            LIMIT $4
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(vector);
        command.Parameters.AddWithValue(opts.ModelName);
        command.Parameters.AddWithValue(opts.Dimensions);
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
