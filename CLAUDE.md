# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

<!-- BEGIN BEADS INTEGRATION v:1 profile:minimal hash:6cd5cc61 -->
## Beads Issue Tracker

This project uses **bd (beads)** for issue tracking. Run `bd prime` to see full workflow context and commands.

### Quick Reference

```bash
bd ready              # Find available work
bd show <id>          # View issue details
bd update <id> --claim  # Claim work
bd close <id>         # Complete work
```

### Rules

- Use `bd` for ALL task tracking — do NOT use TodoWrite, TaskCreate, or markdown TODO lists
- Run `bd prime` for detailed command reference and session close protocol
- Use `bd remember` for persistent knowledge — do NOT use MEMORY.md files

**Architecture in one line:** issues live in a local Dolt DB; sync uses `refs/dolt/data` on your git remote; `.beads/issues.jsonl` is a passive export. See https://github.com/gastownhall/beads/blob/main/docs/SYNC_CONCEPTS.md for details and anti-patterns.

## Agent Context Profiles

The managed Beads block is task-tracking guidance, not permission to override repository, user, or orchestrator instructions.

- **Conservative (default)**: Use `bd` for task tracking. Do not run git commits, git pushes, or Dolt remote sync unless explicitly asked. At handoff, report changed files, validation, and suggested next commands.
- **Minimal**: Keep tool instruction files as pointers to `bd prime`; use the same conservative git policy unless active instructions say otherwise.
- **Team-maintainer**: Only when the repository explicitly opts in, agents may close beads, run quality gates, commit, and push as part of session close. A current "do not commit" or "do not push" instruction still wins.

## Session Completion

This protocol applies when ending a Beads implementation workflow. It is subordinate to explicit user, repository, and orchestrator instructions.

1. **File issues for remaining work** - Create beads for anything that needs follow-up
2. **Run quality gates** (if code changed) - Tests, linters, builds
3. **Update issue status** - Close finished work, update in-progress items
4. **Handle git/sync by active profile**:
   ```bash
   # Conservative/minimal/default: report status and proposed commands; wait for approval.
   git status

   # Team-maintainer opt-in only, unless current instructions forbid it:
   git pull --rebase
   git push
   git status
   ```
5. **Hand off** - Summarize changes, validation, issue status, and any blocked sync/commit/push step

**Critical rules:**
- Explicit user or orchestrator instructions override this Beads block.
- Do not commit or push without clear authority from the active profile or the current user request.
- If a required sync or push is blocked, stop and report the exact command and error.
<!-- END BEADS INTEGRATION -->

## What this project is

`semantic-grep` (CLI name: `segrep`) is a prototype CLI that searches across many documents (PDF, DOCX, XLSX, PPTX, images, etc.) by meaning rather than exact text, then returns or composes answers grounded in the matched content. It is intentionally built as a CLI-only tool — no GUI/web layer — to keep the focus on the retrieval design, not application boilerplate. See `README.md` for the original framing.

The target design described below is now **implemented** end-to-end: ingestion, hybrid retrieval, and generation all exist as real code. The Spectre.Console.Cli quick-start scaffolding (`greet`/`list-friends`) has been removed. When a design detail is ambiguous, the code in `Segrep/` is the source of truth.

## Build & run

```bash
dotnet build                                            # build the solution
dotnet run --project Segrep -- <args>                   # run the CLI, e.g.:
dotnet run --project Segrep -- configure                # set service credentials interactively
dotnet run --project Segrep -- status                   # check connectivity to all services
dotnet run --project Segrep -- index ./docs             # parse → chunk → embed → store a folder
dotnet run --project Segrep -- ask "What are the key risks in the reports?"
dotnet run --project Segrep -- enrich "Summarise Q3 results" --raw
dotnet run --project Segrep -- find "Keter"             # exact occurrences + page numbers
dotnet run --project Segrep -- list                     # show indexed documents
dotnet run --project Segrep -- clear                    # delete all indexed chunks
dotnet run --project Segrep -- update --check           # check for / install a newer release
dotnet run --project Segrep -- document add notes.md    # store a structured markdown document
dotnet run --project Segrep -- document list            # show stored structured documents
```

`segrep` needs three external services configured (see **Configuration** below): Azure Document Intelligence, Azure OpenAI (chat + embeddings), and a PostgreSQL instance with the `vector` and `pg_trgm` extensions. Run `configure` first, then `status` to confirm connectivity. On startup `Program.cs` applies the DB schema via `SchemaMigrator` when a Postgres connection string is present (warns, doesn't fail, if the DB is unreachable).

There is no test project yet; no test runner is configured. The solution is a single project, `Segrep/Segrep.csproj`.

## CLI commands

Registered in `Segrep/Program.cs`; implemented under `Segrep/Commands/`:

- `index [<path>] [--force]` — ingest a folder or file: parse → chunk → embed → store. Skips files whose content hash is unchanged unless `--force`. Defaults to the current directory.
- `enrich <prompt> [--raw] [--top-k N]` — retrieve context and print an augmented prompt to **stdout**, ready to pipe into another LLM. `--raw` embeds the prompt verbatim; otherwise the Interpreter Model expands it into a search query first.
- `ask <prompt> [--top-k N]` — retrieve context and print an LLM-composed answer with `[source: <filename> #<chunk>, p. <page>]` citations, rendered in Spectre panels.
- `find <term> [--doc <name>]` — count exact (word-boundary, case-insensitive) occurrences of a term across indexed documents, reported per document with page numbers; `--doc` restricts to documents whose file name contains the given text.
- `configure` — interactively set Azure Document Intelligence, Azure OpenAI, and PostgreSQL credentials (persisted via .NET User Secrets).
- `status` — check connectivity to all configured services.
- `list` — show all indexed documents.
- `clear` — delete all indexed chunks from the database.
- `update [--check] [--force]` — self-update: compare the running version against the latest GitHub Release, download the matching self-contained binary for this OS/arch, verify its SHA-256 against the release's `SHA256SUMS`, and overwrite the running executable in place (Unix only). `--check` reports availability without installing; `--force` reinstalls even when current.
- `document` — subcommand branch (the codebase's only `AddBranch`) managing **structured markdown documents**: a relational heading-tree store, completely separate from the `ai_doc_chunk` semantic pipeline (no chunking, no embeddings).
  - `document add <path>` — parse (`Documents/MarkdownDocumentParser`, Markdig-based) + validate + store; a same-name (case-insensitive) document is erased and replaced (cascade delete, new id). Validation failures print red line-numbered errors and exit 1: exactly one H1 and it must be the first content in the file (H1 text = document name); no skipped heading levels (first section after H1 is H2, each heading at most one level deeper than the previous, H1–H6); non-empty heading titles. Duplicate sibling titles are allowed. Section body = markdown between its heading and the next top-level heading; H1 preamble is stored on the document row.
  - `document list` — table of Id (Guid) and Name.
  - `document remove <guid>` — delete one document by id (exit 1 if not found).
  - `document clear` — delete all documents; y/n confirmation, `-y|--yes` to skip.

## Architecture

The detailed dependency graph lives in `docs/task-graph.md`; read it before adding a command. Summary of the implemented pipeline:

**Ingestion** (`index`): `DocumentIntelligence/DocumentParser` sends each file to Azure Document Intelligence and gets back Markdown plus a `PageMap` (per-page character spans from the AnalyzeResult mapping Markdown offsets → source page numbers), SHA-256-hash-keyed and cached to disk under the configured `CachePath` as `{hash}.md` + `{hash}.pages.json` (a cache entry missing the pages file is re-analyzed) → `Chunking/MarkdownChunker` splits the Markdown into overlapping windows (~3200 chars / ~400 char overlap, split at blank lines and headings) while tracking each chunk's character range so the `PageMap` yields per-chunk `page_start`/`page_end` → `Embeddings/EmbeddingPipeline` embeds each chunk → rows are upserted into the `ai_doc_chunk` table (`Store/`). Uniqueness is `(file_path, file_hash, chunk_index, model_name)`; on conflict the page columns are refreshed, so `--force` backfills pages on rows indexed before page tracking existed.

**Retrieval** (`enrich`/`ask`): `InterpreterService.InterpretPromptAsync` makes a single LLM call that **decomposes** the prompt into 1–3 ordered `QueryTask`s (JSON `tasks` array; falls back to one Focused task with the verbatim prompt on any failure). Each task carries an intent (`QueryIntent.Focused`, `CorpusWide`, or `ExactTerm`), a self-contained sub-question, a search query (for `ExactTerm` the bare term), and an optional document filter (file-name fragment, e.g. "Kaplan's book" → "kaplan", applied as `file_name ILIKE`). Single-task prompts behave exactly as before; **compound prompts** (e.g. "count Keter in Kaplan's book AND summarize all books in a table") run every sub-task through `InterpreterModel/SubTaskExecutor` and feed all sections into one fused composition call (`ComposeCompoundAnswerAsync`) that must reproduce exact-term counts verbatim (marked authoritative) and cover every part of the question — one Answer panel. **Focused** path: `Search/HybridSearch` fans out to three legs in parallel — `SemanticSearch` (pgvector HNSW cosine ANN), `FullTextSearch` (`tsvector`/`ts_rank`), and `GrepSearch` (`pg_trgm` `similarity` + `ILIKE`) — fuses the ranked lists with **Reciprocal Rank Fusion** (RRF, constant `K = 60`), then applies a per-document diversity cap (max 2 chunks/doc, backfilled from fused order) and takes the top-K. **Corpus-wide** path (questions that must cover every document, e.g. "summarize each PDF"): `SemanticSearch.SearchPerDocumentAsync` uses one window-function query to pull the first chunk plus top-similarity chunks *per document*, and answers/prompts are built grouped per file (`ContextFormatter.BuildGrouped`) with the full document list. **Exact-term** path (count/locate literal occurrences, e.g. "how many times does Keter appear and on what pages"): `Search/TermSearch` prefilters candidate documents in SQL (`ILIKE`, trigram-indexed), then counts word-boundary regex matches against the full cached Markdown and maps each match offset to a page via the `PageMap` — exact counts with no top-K truncation or chunk-overlap double counting; if a document's parse cache is missing it falls back to scanning stored chunks with suffix/prefix overlap dedup and chunk-level page attribution, flagged approximate. For a single exact-term task `ask` renders the occurrence table directly (no LLM composition), `enrich` emits the occurrence summary as context, and `find` invokes it explicitly (`--doc` filters by file name). `enrich` stops after augmentation (`--raw` skips all LLM calls and uses the focused path verbatim); `ask` calls `InterpreterModel/InterpreterService` (an `IChatClient`) to compose a cited answer via `ComposeAnswerAsync`/`ComposeCorpusAnswerAsync`.

**Storage** (`Store/Schema.sql`, embedded resource): a single `ai_doc_chunk` table with a generated `file_name` column, a `content_tsv` `tsvector`, a `vector(1536)` embedding, and nullable `page_start`/`page_end` page attribution, indexed by HNSW (embedding), GIN trigram (`chunk_text`), and GIN (`content_tsv`).

**Structured documents** (`document` branch): `Documents/MarkdownDocumentParser` parses a markdown file to a Markdig AST, validates the heading structure (collecting line-numbered errors thrown as `DocumentFormatException`), and builds a `ParsedDocument`/`ParsedSection` tree from the top-level headings (headings inside code fences, quotes, or lists stay in body text). `Store/DocumentStore` persists the tree into `ai_document` + `ai_document_section` (uuid PKs via `gen_random_uuid()`, adjacency list via `parent_id`, `ON DELETE CASCADE`, unique index on `lower(name)` for case-insensitive replace); `AddAsync` runs delete-old + insert-all in one transaction and reports whether a previous version was replaced. Commands live in `Commands/Document*.cs` and are registered via `config.AddBranch("document", ...)` in `Program.cs`.

**Config/DI**: `Configuration/AppConfiguration` builds config from `appsettings[.env].json` → User Secrets → environment variables, and binds `PostgresOptions`, `AzureDocumentIntelligenceOptions`, `AzureOpenAIOptions`, `EmbeddingModelOptions`. Each subsystem exposes a `ServiceCollectionExtensions.Add…()` registration called from `Program.cs`. Spectre resolves command dependencies through `Infrastructure/DependencyInjectionRegistrar` + `DependencyInjectionResolver` bridging Spectre.Console.Cli to `Microsoft.Extensions.DependencyInjection`.

`docs/ai-semantic-memory.md` is a design RFC for a *different*, larger F#/.NET codebase (`boardwise.ai`, Marten/Postgres, Azure OpenAI, Wolverine). Use it only as a reference for retrieval/ingestion patterns (chunking, pgvector schema, RRF, visibility filtering) — none of its file paths or services exist here.

## Configuration

Config sources (later wins): `appsettings.json` → `appsettings.{DOTNET_ENVIRONMENT}.json` → .NET User Secrets → environment variables. Bound sections (`SectionName` on each options class):

- `Postgres` — `ConnectionString`.
- `AzureDocumentIntelligence` — endpoint, key, `CachePath` for parsed Markdown.
- `AzureOpenAI` — chat/interpreter model endpoint + key.
- `EmbeddingModel` — embedding deployment (dimension 1536, matching the schema).

`configure` writes these to User Secrets (`UserSecretsId` in the `.csproj`); prefer it over hand-editing files for credentials.

## Conventions

- CLI commands are registered via `config.AddCommand<TCommand>("name").WithDescription(...)` in `Segrep/Program.cs` and implemented as `AsyncCommand<TSettings>` subclasses under `Segrep/Commands/`, with **primary-constructor dependency injection** (services injected as constructor params) and logic in `ExecuteAsync`. Settings classes use `[CommandArgument]`/`[CommandOption]` plus `[System.ComponentModel.Description]`.
- Each subsystem folder (`DocumentIntelligence`, `Documents`, `Embeddings`, `InterpreterModel`, `Store`) owns a `ServiceCollectionExtensions` with its DI registration; add new services there, not inline in `Program.cs`.
- User-facing output uses Spectre.Console markup (`AnsiConsole`, panels, progress). `enrich` is the exception — it writes the augmented prompt to raw stdout so it's pipeable.
- Target framework is `net10.0` with nullable reference types and implicit usings enabled.
