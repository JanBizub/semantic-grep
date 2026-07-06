# Task Graph

> **Edge direction:** `A --> B` means **A depends on B** (B must be done first).
> Leaves (e.g. `App Configuration`) are the foundational work; the epic sits at the top.
> This maps to beads as "A is blocked by B".

**Commands**
- `index [<path>]` — ingest a folder (all supported files) or a single file: parse → chunk → embed → store.
- `enrich <prompt> [--raw]` — retrieve relevant context and return the prompt augmented with it (a ready-to-pipe prompt). Default uses the Interpreter Model to interpret/expand the prompt; `--raw` embeds it verbatim and skips the LLM.
- `ask <prompt>` — retrieve relevant context and return an LLM-composed answer with `[source: file #chunk]` citations.

```mermaid

flowchart TD
    EPIC1["Given a prompt LLM searches supporting files for information related to prompt by meaning and returns result enriched for that information"]

    %% Entry points (CLI orchestration)
    TASK9["index Command (ingestion entry point)"]
    TASK17["enrich Command (augmented-prompt entry point)"]
    TASK19["ask Command (answer + citations entry point)"]

    %% Retrieval / search flow
    TASK12["Interpreter Model"]
    TASK1["Results Aggregation (RRF fusion)"]
    TASK2["Semantic Search"]
    TASK4["Full Text Search (tsvector / ts_rank)"]
    TASK5["Grep (pg_trgm ILIKE / regex)"]
    TASK3["Vector Data"]

    %% Ingestion flow
    TASK6["Embedding data from chunked files"]
    TASK7["Chunked Data From Files"]
    TASK10["Azure Document Intelligence as file parser"]

    %% Persistence (pgsql + pgvector)
    TASK8["PostgreSQL + pgvector Store (schema and migrations)"]

    %% Models
    TASK11["Embedding Model"]
    TASK13["Embedding Model Service Registration"]
    TASK14["Embedding Model Configuration"]
    TASK22["Interpreter Model Service Registration"]
    TASK23["Interpreter Model Configuration"]
    TASK18["Azure Document Intelligence Service Registration"]
    TASK16["Azure Document Intelligence Configuration"]

    %% Foundation
    TASK15["App Configuration"]

    %% --- Epic decomposition (three commands) ---
    EPIC1 --> TASK9
    EPIC1 --> TASK17
    EPIC1 --> TASK19

    %% --- enrich command ---
    TASK17 --> TASK1
    TASK17 --> TASK12

    %% --- ask command ---
    TASK19 --> TASK1
    TASK19 --> TASK12

    %% --- Interpreter model ---
    TASK12 --> TASK22
    TASK12 --> TASK23
    TASK23 --> TASK15

    %% --- Aggregation: three hybrid legs ---
    TASK1 --> TASK2
    TASK1 --> TASK4
    TASK1 --> TASK5

    %% --- Semantic search (needs embedder + store) ---
    TASK2 --> TASK3
    TASK2 --> TASK13
    TASK2 --> TASK8

    %% --- Lexical legs query the stored chunk text ---
    TASK4 --> TASK8
    TASK5 --> TASK8

    %% --- Vector data: populated by embedding pipeline, lives in store ---
    TASK3 --> TASK6
    TASK3 --> TASK8

    %% --- Index command / ingestion pipeline ---
    TASK9 --> TASK6
    TASK6 --> TASK7
    TASK6 --> TASK13
    TASK6 --> TASK8
    TASK7 --> TASK10

    %% --- Azure Document Intelligence ---
    TASK10 --> TASK16
    TASK10 --> TASK18
    TASK16 --> TASK15

    %% --- Embedding model ---
    TASK13 --> TASK11
    TASK13 --> TASK14
    TASK14 --> TASK15

    %% --- Store config ---
    TASK8 --> TASK15
```
