using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;
using Segrep.Chunking;
using Segrep.Configuration;

namespace Segrep.Embeddings;

public sealed class EmbeddingPipeline(
    EmbeddingService embeddingService,
    IOptions<EmbeddingModelOptions> modelOptions,
    NpgsqlDataSource dataSource)
{
    public async Task<bool> IsUpToDateAsync(string filePath, string fileHash, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM ai_doc_chunk WHERE file_path = $1 AND file_hash = $2)";
        cmd.Parameters.AddWithValue(filePath);
        cmd.Parameters.AddWithValue(fileHash);
        return (bool)(await cmd.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<string?> FindDuplicatePathAsync(string filePath, string fileHash, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT file_path FROM ai_doc_chunk WHERE file_hash = $1 AND file_path <> $2 LIMIT 1";
        cmd.Parameters.AddWithValue(fileHash);
        cmd.Parameters.AddWithValue(filePath);
        return (string?)await cmd.ExecuteScalarAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(string FileName, string FileHash, int ChunkCount)>> ListDocumentsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT file_name, file_hash, COUNT(*)::int
            FROM ai_doc_chunk
            GROUP BY file_name, file_hash
            ORDER BY file_name
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var results = new List<(string, string, int)>();
        while (await reader.ReadAsync(cancellationToken))
            results.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));

        return results;
    }

    public async Task IngestAsync(IReadOnlyList<Chunk> chunks, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
            return;

        var opts = modelOptions.Value;
        var texts = chunks.Select(c => c.Text).ToList();
        var embeddings = await embeddingService.EmbedAsync(texts, cancellationToken);
        progress?.Report(0.5);

        // Group by file so we can delete stale entries per file in one pass.
        var byFile = chunks
            .Select((chunk, i) => (chunk, embedding: embeddings[i]))
            .GroupBy(x => x.chunk.FilePath);

        var upserted = 0;
        foreach (var fileGroup in byFile)
        {
            var filePath = fileGroup.Key;
            var items = fileGroup.ToList();
            var fileHash = items[0].chunk.FileHash;

            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            // Remove records for this file that belong to a different (older) content hash.
            await DeleteStaleAsync(connection, transaction, filePath, fileHash, cancellationToken);

            foreach (var (chunk, embedding) in items)
            {
                await UpsertChunkAsync(connection, transaction, chunk, embedding.Vector.ToArray(), opts, cancellationToken);
                upserted++;
                progress?.Report(0.5 + 0.5 * upserted / chunks.Count);
            }

            // Commit only after every chunk for this file lands, so cancellation mid-loop
            // rolls back instead of leaving a partial chunk set that IsUpToDateAsync would
            // later mistake for a complete, already-indexed file.
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task DeleteStaleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string filePath,
        string currentHash,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "DELETE FROM ai_doc_chunk WHERE file_path = $1 AND file_hash <> $2";
        cmd.Parameters.AddWithValue(filePath);
        cmd.Parameters.AddWithValue(currentHash);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertChunkAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Chunk chunk,
        float[] floatVector,
        EmbeddingModelOptions opts,
        CancellationToken cancellationToken)
    {
        // On conflict the content is identical (hash + index match); refresh the page
        // attribution so --force re-indexing backfills pages on rows stored before
        // page tracking existed.
        const string sql = """
            INSERT INTO ai_doc_chunk (file_path, file_hash, chunk_index, chunk_text, content_tsv, model_name, dim, embedding, page_start, page_end)
            VALUES ($1, $2, $3, $4, to_tsvector('english', $4), $5, $6, $7, $8, $9)
            ON CONFLICT (file_path, file_hash, chunk_index, model_name)
            DO UPDATE SET page_start = EXCLUDED.page_start, page_end = EXCLUDED.page_end
            """;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue(chunk.FilePath);
        cmd.Parameters.AddWithValue(chunk.FileHash);
        cmd.Parameters.AddWithValue(chunk.ChunkIndex);
        cmd.Parameters.AddWithValue(chunk.Text);
        cmd.Parameters.AddWithValue(opts.ModelName);
        cmd.Parameters.AddWithValue(floatVector.Length);
        cmd.Parameters.AddWithValue(new Vector(floatVector));
        cmd.Parameters.Add(new NpgsqlParameter<int?> { TypedValue = chunk.PageStart });
        cmd.Parameters.Add(new NpgsqlParameter<int?> { TypedValue = chunk.PageEnd });
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
