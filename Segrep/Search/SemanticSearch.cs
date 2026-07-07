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
        var vector = await EmbedQueryAsync(query, cancellationToken);

        const string sql = """
            SELECT id, file_path, file_hash, chunk_index, chunk_text,
                   (1 - (embedding <=> $1))::double precision AS score,
                   page_start, page_end
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

        return await ReadResultsAsync(command, cancellationToken);
    }

    /// <summary>
    /// Retrieves chunks from every indexed document: the first chunk of each document
    /// (intro/abstract — the best summarization context) plus the top chunks per document
    /// by cosine similarity to the query.
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchPerDocumentAsync(
        string query,
        int perDocTopK = 3,
        CancellationToken cancellationToken = default)
    {
        var opts = modelOptions.Value;
        var vector = await EmbedQueryAsync(query, cancellationToken);

        const string sql = """
            WITH ranked AS (
                SELECT id, file_path, file_hash, chunk_index, chunk_text,
                       (1 - (embedding <=> $1))::double precision AS score,
                       page_start, page_end,
                       ROW_NUMBER() OVER (PARTITION BY file_name ORDER BY embedding <=> $1) AS sim_rank,
                       ROW_NUMBER() OVER (PARTITION BY file_name ORDER BY chunk_index)      AS pos_rank
                FROM ai_doc_chunk
                WHERE model_name = $2 AND dim = $3
            )
            SELECT id, file_path, file_hash, chunk_index, chunk_text, score, page_start, page_end
            FROM ranked
            WHERE pos_rank = 1 OR sim_rank <= $4
            ORDER BY file_path, chunk_index
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(vector);
        command.Parameters.AddWithValue(opts.ModelName);
        command.Parameters.AddWithValue(opts.Dimensions);
        command.Parameters.AddWithValue(Math.Max(0, perDocTopK - 1));

        return await ReadResultsAsync(command, cancellationToken);
    }

    private async Task<Vector> EmbedQueryAsync(string query, CancellationToken cancellationToken)
    {
        var embeddings = await embeddingService.EmbedAsync([query], cancellationToken);
        return new Vector(embeddings[0].Vector.ToArray());
    }

    private static async Task<IReadOnlyList<SearchResult>> ReadResultsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
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
