CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE TABLE IF NOT EXISTS ai_doc_chunk (
    id bigserial PRIMARY KEY,
    file_path text NOT NULL,
    file_name text GENERATED ALWAYS AS (substring(file_path from '([^/\\]+)$')) STORED,
    file_hash text NOT NULL,
    chunk_index integer NOT NULL,
    chunk_text text NOT NULL,
    content_tsv tsvector NOT NULL,
    model_name text NOT NULL,
    dim integer NOT NULL,
    embedding vector(1536) NOT NULL,
    page_start integer,
    page_end integer,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ai_doc_chunk_unique UNIQUE (file_path, file_hash, chunk_index, model_name)
);

ALTER TABLE ai_doc_chunk
    ADD COLUMN IF NOT EXISTS file_name text
    GENERATED ALWAYS AS (substring(file_path from '([^/\\]+)$')) STORED;

ALTER TABLE ai_doc_chunk ADD COLUMN IF NOT EXISTS page_start integer;
ALTER TABLE ai_doc_chunk ADD COLUMN IF NOT EXISTS page_end integer;

CREATE INDEX IF NOT EXISTS ai_doc_chunk_embedding_hnsw_idx
    ON ai_doc_chunk USING hnsw (embedding vector_cosine_ops);

CREATE INDEX IF NOT EXISTS ai_doc_chunk_chunk_text_trgm_idx
    ON ai_doc_chunk USING gin (chunk_text gin_trgm_ops);

CREATE INDEX IF NOT EXISTS ai_doc_chunk_content_tsv_idx
    ON ai_doc_chunk USING gin (content_tsv);

CREATE TABLE IF NOT EXISTS ai_document (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name text NOT NULL,
    preamble text NOT NULL DEFAULT '',
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ai_document_name_lower_ux ON ai_document (lower(name));

CREATE TABLE IF NOT EXISTS ai_document_section (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id uuid NOT NULL REFERENCES ai_document(id) ON DELETE CASCADE,
    parent_id uuid REFERENCES ai_document_section(id) ON DELETE CASCADE,
    title text NOT NULL,
    content text NOT NULL DEFAULT '',
    level integer NOT NULL,          -- 2..6 (H2..H6)
    position integer NOT NULL,       -- sibling order, 0-based
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ai_document_section_doc_ix ON ai_document_section (document_id);
CREATE INDEX IF NOT EXISTS ai_document_section_parent_ix ON ai_document_section (parent_id);
