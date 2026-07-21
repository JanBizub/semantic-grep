# Code Review — segrep (2026-07-09)

Full review of the `Segrep/` codebase: technical quality, best practices, and functional behavior.
Build verified clean (`dotnet build`, 0 warnings, 0 errors). Findings marked **[verified]** were
reproduced empirically with a scratch harness; the rest come from code inspection.

**Overall assessment:** the codebase is in very good shape for a prototype. The architecture is
clean and layered (parse → chunk → embed → store / three-leg hybrid retrieval → RRF → compose),
SQL is parameterized everywhere, the page-attribution design (`PageMap` + offset-tracking chunker)
is genuinely well thought out, and the exact-term path correctly avoids the classic
chunk-overlap double-counting trap. The issues below are mostly edge cases and functional gaps,
plus two data-integrity bugs worth fixing before relying on the index.

---

## 1. High-impact functional findings

### F1. Cancelled ingest leaves a document half-indexed but marked "up to date"
`EmbeddingPipeline.IngestAsync` (`Segrep/Embeddings/EmbeddingPipeline.cs:55`) deletes the stale
rows for a file (`DeleteStaleAsync`) and then inserts the new chunks one by one — with **no
transaction**. If the user presses Esc (which `IndexCommand` explicitly supports) or the process
dies mid-file:

- old rows are already deleted, only a prefix of the new chunks exists;
- `IsUpToDateAsync` (`EmbeddingPipeline.cs:14`) only checks `EXISTS(file_path AND file_hash)`, so
  the *partial* document now passes the freshness check;
- every subsequent `index` run silently skips it ("already in database"), and `ask`/`find`
  operate on truncated content with no indication anything is wrong.

**Fix:** wrap delete + inserts per file in one transaction (a file's chunks are small enough for
a single transaction; batching inserts would also cut round-trips). Alternatively insert first,
delete stale last.

### F2. Switching the embedding model silently empties semantic search
`IsUpToDateAsync` ignores `model_name`, but `SemanticSearch` filters
`WHERE model_name = $2 AND dim = $3` (`Segrep/Search/SemanticSearch.cs:25`). Change
`EmbeddingModel:ModelName` in config and:

- `index` reports every file as "already in database — skipped";
- semantic search returns **zero rows** (only the old model's embeddings exist);
- hybrid search quietly degrades to FTS + trigram only, with no warning.

**Fix:** include `model_name` (and `dim`) in the `IsUpToDateAsync` predicate. Consider also
warning at query time when the table contains no rows for the configured model.

### F3. Chunker emits a duplicate, overlap-only trailing chunk **[verified]**
In `MarkdownChunker.BuildWindows` (`Segrep/Chunking/MarkdownChunker.cs:94`), `Flush()` seeds the
buffer with the overlap tail of the window just flushed. If the *last* block triggers a flush
(any document whose final block lands on the `buffer.Length >= TargetChars` path), the loop ends
with the buffer holding only that overlap, and the post-loop `if (buffer.Length > 0)` emits it as
a final window. Reproduced: a single 5,489-char paragraph produces 2 chunks, where chunk #1
(399 chars) is entirely contained in chunk #0.

Consequences: a wasted embedding + row per affected document, duplicate text in retrieved
context, and inflated counts in the `TermSearch` chunk-fallback path when the duplicate exceeds
`MaxChunkOverlap` heuristics.

**Fix:** track whether the buffer contains anything *beyond* the seeded overlap; only emit the
final window if it does.

### F4. Oversized blocks are never split down to the target size **[verified]**
`BuildWindows` flushes an over-target buffer as a single window but never splits a single block
that is itself larger than `TargetChars`. A 5,489-char paragraph became one 5,489-char chunk.
Azure Document Intelligence routinely emits large tables/paragraphs without blank lines; a very
large block (e.g. a big spreadsheet rendered as one Markdown table) becomes one giant chunk that
can exceed the embedding model's token limit (~8k tokens for `text-embedding-3-*`) and fail the
whole file's ingestion — or, short of that, produce a chunk too diluted to retrieve well.

**Fix:** hard-split blocks larger than `TargetChars` (at line breaks, then at whitespace) before
windowing.

### F5. The interpreter's `document` filter is only honored for exact-term tasks
The interpreter is instructed to emit a `document` file-name fragment for *any* sub-request that
names a specific document (`InterpreterService.cs:24`), but `QueryTask.DocumentFilter` is only
applied in the `TermSearch` path. `HybridSearch.SearchAsync` and
`SemanticSearch.SearchPerDocumentAsync` take no filter (`SubTaskExecutor.cs:25,34`,
`AskCommand.cs:53-55`). So "summarize chapter 3 of Kaplan's book" searches the entire corpus and
can happily answer from a different book if it ranks higher.

**Fix:** thread `DocumentFilter` into all three search legs (`file_name ILIKE '%' || $n || '%'`
is already indexed via the generated column) and into `SearchPerDocumentAsync`.

### F6. Corpus-wide retrieval is unbounded — context blows up with corpus size
`SearchPerDocumentAsync` returns first-chunk + top-3 per document for *every* document with no
cap on the number of documents (`SemanticSearch.cs:46`). At ~3,200 chars/chunk, 50 indexed
documents ≈ 480k+ characters of context in a single composition call — token-limit failures or
severe cost, with no guard or warning. Related smaller issue: `--top-k` is silently ignored on
the corpus-wide and exact-term paths (`AskCommand.cs:54`, `perDocTopK: 3` hardcoded).

**Fix:** cap or paginate the corpus-wide path (e.g. warn and truncate above N documents, or
map-reduce: summarize per document, then fuse). Honor `--top-k` as the per-document budget.

### F7. Duplicate content under two paths is handled inconsistently
Indexing the same bytes under two paths warns (`FindDuplicatePathAsync`) but still indexes both.
Downstream, each feature then makes a different choice:

- `TermSearch` dedupes by `file_hash` — counted once;
- corpus-wide search partitions by `file_name` — counted once *only if the names collide*,
  twice otherwise;
- hybrid search and `list` treat them as distinct documents.

Also, `SearchPerDocumentAsync` partitioning by `file_name` means two *different* documents that
share a base name (`a/report.pdf`, `b/report.pdf`) are merged into one partition and one of them
loses its "first chunk" representative.

**Fix:** pick one identity rule (file_hash for "same document", file_path for "same file") and
apply it consistently; partition corpus-wide by `file_path`, group display by name.

### F8. Lifecycle gaps: no per-document removal, cache never cleaned
- Deleting/renaming a source file leaves its chunks in the index forever; the only remedy is
  `clear` (everything) — there is no `remove <doc>` or `index --prune`.
- The Document Intelligence cache (`di-cache`) grows unboundedly and survives `clear`; nothing
  ever evicts entries whose documents are gone.
- `list` shows name/hash/chunks but not page counts or indexed-at, which would help diagnose
  F1/F2-type staleness.

### F9. Unconfigured services surface as raw exceptions
Running `ask`/`enrich`/`index` before `configure` throws from deep inside DI:
`AzureOpenAIOptions.NormalizeEndpoint("")` → `UriFormatException`;
`new Uri(options.Endpoint)` in the Document Intelligence registration does the same. The user
gets a stack trace instead of "Azure OpenAI is not configured — run `segrep configure`". The
options classes already expose `IsConfigured`; commands never check it.

**Fix:** validate configuration at command start (or via `OptionsBuilder.Validate`) and print an
actionable message.

### F10. Interpreter edge cases
- `ParseInterpretation` takes at most 3 tasks (`InterpreterService.cs:159`) and the system prompt
  asks for at most 3, so a 4-part question silently drops parts — the compound composition
  prompt then demands "answer EVERY part", which the model can't do from missing context.
- For a *single* exact-term task, `ask` renders the occurrence table directly and never composes
  an answer (`AskCommand.cs:38-46`). Good for "how many times does X appear", but a comparative
  question the interpreter classifies as one EXACT_TERM task ("does Keter appear more often in
  book A or B?") gets a raw table, not an answer to the actual question.
- The blanket `catch` in `InterpretPromptAsync` (`InterpreterService.cs:69`) also swallows
  `OperationCanceledException`, so Ctrl+C during interpretation degrades into running a focused
  search instead of cancelling. Catch specific exceptions, or rethrow when
  `cancellationToken.IsCancellationRequested`.
- In `enrich` (non-`--raw`), interpretation failure degrades silently; since stdout must stay
  clean, consider a stderr note so the user knows decomposition didn't happen.

### F11. `status` reports "Reachable" for any HTTP response — the API key is never exercised
`CheckHttpAsync` (`StatusCommand.cs:66`) does an unauthenticated GET of the endpoint root; a 401
(wrong key) or 404 renders as green "Reachable". The check answers "is the host up", not "will
my calls work", which is what a user runs `status` to learn. A cheap authenticated call (e.g.
list deployments / a 1-token embedding) would validate the credentials it claims to check. Also:
the API key parameter is accepted and unused, and the check ignores the command's
`CancellationToken`.

### F12. Supported file types miss the cheapest cases
`DocumentParser.SupportedExtensions` (`DocumentParser.cs:11`) omits `.md`, `.html`, and source
files — notable for a tool named semantic-*grep* — while `.txt`/`.csv` are sent through Azure
Document Intelligence, paying per page to OCR-layout plain text that could be read directly.
A local-parse fast path (read text, skip DI, synthesize no PageMap) would broaden coverage and
cut cost.

---

## 2. Technical / code-quality findings

### T1. `Program.cs` builds two service providers → two Npgsql pools
`Main` builds a provider for schema migration (`Program.cs:37`), then
`DependencyInjectionRegistrar.Build()` builds a *second* provider from the same
`ServiceCollection` for Spectre. Every singleton — including `NpgsqlDataSource` (a connection
pool) and both Azure clients — is constructed twice. Build the provider once and hand the same
instance to a registrar that wraps it, or run the migration through the registrar-built provider.

### T2. `configure` writes user secrets to the wrong path on Windows
`UserSecretsStore.GetSecretsFilePath` (`UserSecretsStore.cs:16`) hardcodes
`~/.microsoft/usersecrets/...`. The configuration loader (`AddUserSecrets`) resolves
`%APPDATA%\Microsoft\UserSecrets\{id}\secrets.json` when `APPDATA` is set — i.e. always on
Windows. So on Windows, `configure` saves to a file the app never reads. Works on macOS/Linux.
Mirror the loader's logic: use `APPDATA` when present, else `~/.microsoft/usersecrets`.

### T3. Inconsistent ILIKE escaping
- `TermSearch.FindCandidateDocumentsAsync` carefully escapes `%`/`_`/`\` in the term
  (`TermSearch.cs:86`) but not in the `documentFilter` parameter used as
  `file_name ILIKE '%' || $2 || '%'` — a filter containing `%` or `_` matches unintended files.
- `GrepSearch` (`GrepSearch.cs:14`) doesn't escape the pattern at all, so a query containing `%`
  behaves as a wildcard. For a fuzzy leg this is low-severity, but a shared
  `EscapeLike(string)` helper used by all three call sites would remove the inconsistency.

### T4. Schema migration runs on every CLI invocation
`EnsureStoreSchema` executes the full DDL script (extensions, table, three index builds) before
*every* command, including `list` and `configure`, adding a round-trip of latency and requiring
DDL privileges for read-only operations. Consider running it only for commands that write
(`index`, `clear`), or keeping a schema-version marker. Related nit: it blocks with
`.GetAwaiter().GetResult()` — making `Main` `async Task<int>` removes the sync-over-async.

### T5. `dim` is configurable but the column is `vector(1536)`
`EmbeddingModelOptions.Dimensions` flows into requests and the `dim` column, but the schema pins
`embedding vector(1536)` (`Schema.sql:14`). Setting `Dimensions: 3072` fails at insert with a
low-level pgvector dimension error. Either validate `Dimensions == 1536` at startup with a clear
message, or derive the column from config (harder). The `WHERE dim = $3` filters in
`SemanticSearch` are dead weight as long as the column type enforces one dimension.

### T6. Minor correctness nits
- `AskCommand`/`EnrichCommand` don't validate `--top-k >= 1` (contrast: `IndexCommand`
  validates `--parallel`). `--top-k 0` yields "No relevant content found", which reads as a data
  problem rather than an argument problem.
- Mid-stream `Flush()` already appends `"\n\n"` after the overlap seed, and the next block
  append adds another — chunks can contain `\n\n\n\n` (cosmetic; slightly perturbs blank-line
  semantics if the chunk text is ever re-split).
- `MarkdownChunker.Chunk` assigns `ChunkIndex` before filtering out empty chunks, so indices can
  have gaps. Harmless today (uniqueness only), but the fallback overlap-dedup in `TermSearch`
  assumes adjacent `chunk_index` means adjacent text.
- Duplicated reader-mapping boilerplate across `SemanticSearch`, `FullTextSearch`, `GrepSearch`
  (~15 lines × 3) — extract one `ReadSearchResultsAsync` helper.
- `StatusCommand` creates a new `HttpClient` per check — fine for a CLI one-shot, but the
  unused `apiKey` parameter (see F11) hints the auth check was intended and lost.
- Compound sub-tasks run sequentially (`AskCommand.cs:79`, `EnrichCommand.cs:69`); they're
  independent and could run with `Task.WhenAll` for 2–3× latency win on compound questions.

### T7. No tests, no CI
The most consequential technical gap. The pure logic here is *very* testable without any Azure
or Postgres dependency: `MarkdownChunker` (F3/F4 would both have been caught by a property test
"concatenated chunks minus overlap == source"), `PageMap`, `TermSearch.SharedOverlapLength`,
`HybridSearch.Fuse`/`CapPerDocument`, `InterpreterService.ParseInterpretation`,
`AzureOpenAIOptions.NormalizeEndpoint`, `TermOccurrenceFormatter`. A small xUnit project covering
those would lock down the trickiest code in the repo. Search/DB behavior can follow later with
Testcontainers.

---

## 3. What's done well

- **Security hygiene:** every SQL statement is parameterized; user input reaching regex goes
  through `Regex.Escape`; Spectre markup is consistently escaped (`Markup.Escape`) before
  rendering user-derived strings; credentials live in User Secrets, not files in the repo.
- **Exact-term design** (`TermSearch`): SQL trigram prefilter → full-Markdown regex scan →
  PageMap offset mapping is the right shape — exact counts, no top-K truncation — and the
  chunk-fallback path with suffix/prefix overlap dedup plus an explicit "approximate" flag shown
  to the user is honest engineering.
- **Page attribution:** tracking original-markdown character ranges through the chunker so the
  `PageMap` can attribute pages, and refreshing page columns on upsert conflict so `--force`
  backfills old rows, is a thoughtful migration story.
- **RRF fusion** is implemented correctly (per-leg rank, `1/(K+rank+1)`, K=60), and the
  per-document diversity cap with backfill is a sensible, well-commented touch.
- **Graceful degradation:** interpreter failure falls back to a plain focused search; missing
  parse cache falls back to chunk scanning; missing DB at startup warns instead of dying.
- **CLI craft:** `enrich` keeping stdout clean for piping, Esc-to-cancel with per-file progress,
  `clear -y` confirmation, "no occurrences" hint pointing at `list`/`index` — all good
  clig.dev-style behavior.
- Consistent conventions throughout: primary-constructor DI, per-subsystem
  `ServiceCollectionExtensions`, options-pattern config, `sealed` types, nullable enabled.

---

## 4. Prioritized recommendations

| # | Action | Fixes | Effort |
|---|--------|-------|--------|
| 1 | Transaction around per-file delete+insert in `IngestAsync` | F1 | Small |
| 2 | Add `model_name` to `IsUpToDateAsync` predicate | F2 | Tiny |
| 3 | Fix trailing overlap-only window + hard-split oversized blocks in `MarkdownChunker` | F3, F4 | Small |
| 4 | Add a unit-test project; start with chunker/PageMap/RRF/parse tests | T7 | Medium |
| 5 | Thread `DocumentFilter` into hybrid + corpus-wide search | F5 | Small |
| 6 | Configuration guards with actionable errors in commands | F9 | Small |
| 7 | Cap/staged corpus-wide retrieval; honor `--top-k` there | F6 | Medium |
| 8 | Single service provider in `Program.cs` | T1 | Small |
| 9 | Authenticated `status` checks | F11 | Small |
| 10 | Local fast-path parser for `.txt`/`.md` (skip Azure DI) | F12 | Medium |

Everything else (escaping helper, per-document removal, cache eviction, Windows secrets path,
parallel sub-tasks) is worth beads-tracking as backlog items.
