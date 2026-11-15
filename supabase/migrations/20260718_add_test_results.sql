-- ============================================================
-- Migration: Create test_results table + wordbanks storage bucket
-- Idempotent — safe to re-run
-- ============================================================

-- 1. Create test_results table
CREATE TABLE IF NOT EXISTS test_results (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    timestamp TIMESTAMPTZ NOT NULL DEFAULT now(),
    total_words INTEGER NOT NULL DEFAULT 0,
    correct_count INTEGER NOT NULL DEFAULT 0,
    accuracy DOUBLE PRECISION NOT NULL DEFAULT 0,
    word_list_name TEXT NOT NULL DEFAULT '',
    record_type TEXT NOT NULL DEFAULT 'dictation',
    word_details JSONB DEFAULT '[]',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_test_results_user_id ON test_results(user_id);
CREATE INDEX IF NOT EXISTS idx_test_results_timestamp ON test_results(timestamp);

ALTER TABLE test_results ENABLE ROW LEVEL SECURITY;

DO $$ BEGIN
    CREATE POLICY "Users can view own test results"
        ON test_results FOR SELECT
        USING (auth.uid() = user_id);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    CREATE POLICY "Users can insert own test results"
        ON test_results FOR INSERT
        WITH CHECK (auth.uid() = user_id);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    CREATE POLICY "Users can delete own test results"
        ON test_results FOR DELETE
        USING (auth.uid() = user_id);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- 2. Create wordbanks storage bucket (idempotent)
INSERT INTO storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
VALUES ('wordbanks', 'wordbanks', false, 52428800, '{application/json,text/csv,text/plain}')
ON CONFLICT (id) DO NOTHING;

-- 3. Storage RLS policies for wordbanks bucket (idempotent)
DO $$ BEGIN
    CREATE POLICY "Authenticated users can upload wordbanks"
        ON storage.objects FOR INSERT
        TO authenticated
        WITH CHECK (bucket_id = 'wordbanks');
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    CREATE POLICY "Authenticated users can view own wordbanks"
        ON storage.objects FOR SELECT
        TO authenticated
        USING (bucket_id = 'wordbanks' AND auth.uid()::text = (string_to_array(name, '/'))[1]);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    CREATE POLICY "Authenticated users can delete own wordbanks"
        ON storage.objects FOR DELETE
        TO authenticated
        USING (bucket_id = 'wordbanks' AND auth.uid()::text = (string_to_array(name, '/'))[1]);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;
