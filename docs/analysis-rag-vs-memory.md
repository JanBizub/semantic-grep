# Analysis: Is Simple Semantic Search Sufficient for the RFC Use Cases?

Based on analysis of the AI Semantic Memory RFC, Hindsight's RAG vs Memory comparison, and the `semantic-grep` prototype.

## Verdict

**Simple semantic search is sufficient for UC-1, but not for UC-2.**

**UC-1 (Minutes Generation)** — single-meeting, retrieve-and-summarize — is straightforward. Top-k cosine similarity within a meeting's files, with the transcript chunk, is enough. The RFC confirms this: "UC-1 needs none of this: it stays single-pass retrieve-and-summarise."

**UC-2 (MCP topic memory)** — cross-meeting, entity-rich, temporal — is where pure semantic search breaks down. The RFC explicitly identifies it as the primary justification for building the layer. Three specific failure modes:

1. **Proper nouns, resolution numbers, monetary figures** — embeddings blur these; lexical/grep legs catch them. Your prototype already handles this with the three-leg RRF (semantic + FTS + pg_trgm).

2. **Multi-hop / entity-join** — *"who proposed the budget and did it pass"* requires linking chunks across documents via entity relationships. This is not a retrieval problem — it's a graph traversal problem. But XXXX already owns the governance graph (`Resolution`, `Vote`, `Assignment`, `Attendee`), so the RFC's solution is to expose these as MCP tools and let the calling agent compose them, not to build a new graph engine.

3. **Temporal queries** — *"how has this decision changed since last year"* needs date-parsed range filtering. Again, the structured domain already has `Resolution.ValidFrom`, `AssignmentStatus` history, etc. — direct queries, not vector search.

## Conclusion

You don't need Hindsight-style memory. What you need is exactly what the RFC proposes and what your prototype validates:

| Need | Solution | Status |
|---|---|---|
| Semantic similarity | pgvector cosine | Implemented |
| Lexical precision (names, IDs, figures) | FTS + pg_trgm | Implemented |
| Fusion | RRF (k=60) | Implemented |
| Multi-hop entity traversal | Structured MCP tools over existing projections | RFC §6.5, not yet in prototype |
| Temporal queries | Structured MCP tools over `ValidFrom`/`Status` history | RFC §6.5, not yet in prototype |
| Knowledge consolidation / belief evolution | Not needed — governance data is authoritative via event sourcing | N/A |
| Disposition traits | Not needed — this is a domain tool, not an agent personality | N/A |

The gap between the prototype and the RFC is the **structured MCP tools** (`topic_timeline`, `resolutions_for_topic`, `open_assignments`, `votes_for_resolution`) — which are plain queries against existing Marten projections, not new retrieval infrastructure. The hybrid retrieval already built covers the "long free-text" half; the structured domain covers the rest. Hindsight's "4 parallel retrievals → RRF → rerank" shape is essentially what is already implemented, minus the graph/temporal legs — which XXXX doesn't need as vector searches because it already owns that data as queryable structured records.
