-- ============================================================
-- Migration: Create base profiles table (required by later migrations)
-- Idempotent – safe to re-run
-- ============================================================

CREATE TABLE IF NOT EXISTS profiles (
    id UUID PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    nickname TEXT NOT NULL DEFAULT '',
    full_name TEXT NOT NULL DEFAULT '',
    avatar_url TEXT,
    role TEXT NOT NULL DEFAULT 'user',
    gender TEXT,
    banned BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Admin can view all profiles (app-side admin checks); users' own rows are
-- covered by the RLS policies added in 20260717.
DO $$ BEGIN
    CREATE POLICY "Admins can view all profiles"
        ON profiles FOR SELECT
        USING (
            EXISTS (
                SELECT 1 FROM profiles p
                WHERE p.id = auth.uid() AND p.role = 'admin'
            )
        );
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;
