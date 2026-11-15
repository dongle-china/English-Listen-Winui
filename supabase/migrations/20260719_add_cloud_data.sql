-- ============================================================
-- Migration: Create cloud_data table for cloud sync
-- Idempotent — safe to re-run
-- ============================================================

CREATE TABLE IF NOT EXISTS cloud_data (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    data_type TEXT NOT NULL,
    data_json TEXT NOT NULL DEFAULT '',
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE(user_id, data_type)
);

CREATE INDEX IF NOT EXISTS idx_cloud_data_user_id ON cloud_data(user_id);

ALTER TABLE cloud_data ENABLE ROW LEVEL SECURITY;

DO $$ BEGIN
    CREATE POLICY "Users can view own cloud data"
        ON cloud_data FOR SELECT
        USING (auth.uid() = user_id);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    CREATE POLICY "Users can insert own cloud data"
        ON cloud_data FOR INSERT
        WITH CHECK (auth.uid() = user_id);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    CREATE POLICY "Users can update own cloud data"
        ON cloud_data FOR UPDATE
        USING (auth.uid() = user_id);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    CREATE POLICY "Users can delete own cloud data"
        ON cloud_data FOR DELETE
        USING (auth.uid() = user_id);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;
