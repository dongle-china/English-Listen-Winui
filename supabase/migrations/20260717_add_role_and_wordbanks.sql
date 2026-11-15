-- ============================================================
-- Migration: Add role to profiles + Create wordbanks table
-- Idempotent — safe to re-run
-- ============================================================

-- 1. Add role column to profiles (default 'user')
ALTER TABLE profiles ADD COLUMN IF NOT EXISTS role TEXT NOT NULL DEFAULT 'user';

-- 2. Create wordbanks table
CREATE TABLE IF NOT EXISTS wordbanks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    file_url TEXT NOT NULL,
    file_size BIGINT NOT NULL DEFAULT 0,
    word_count INTEGER NOT NULL DEFAULT 0,
    uploaded_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    description TEXT DEFAULT '',
    content_hash TEXT DEFAULT ''
);

-- 3. Create index for faster queries
CREATE INDEX IF NOT EXISTS idx_wordbanks_user_id ON wordbanks(user_id);

-- 4. Enable RLS on wordbanks
ALTER TABLE wordbanks ENABLE ROW LEVEL SECURITY;

-- 5. RLS policies for wordbanks (idempotent)
DO $$ BEGIN
    CREATE POLICY "Users can view own wordbanks"
        ON wordbanks FOR SELECT
        USING (auth.uid() = user_id);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    CREATE POLICY "Users can insert own wordbanks"
        ON wordbanks FOR INSERT
        WITH CHECK (auth.uid() = user_id);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    CREATE POLICY "Users can delete own wordbanks"
        ON wordbanks FOR DELETE
        USING (auth.uid() = user_id);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    CREATE POLICY "Admins can view all wordbanks"
        ON wordbanks FOR SELECT
        USING (
            EXISTS (
                SELECT 1 FROM profiles
                WHERE profiles.id = auth.uid() AND profiles.role = 'admin'
            )
        );
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- 6. RLS policies for profiles (idempotent)
ALTER TABLE profiles ENABLE ROW LEVEL SECURITY;

DO $$ BEGIN
    CREATE POLICY "Users can view own profile"
        ON profiles FOR SELECT
        USING (auth.uid() = id);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    CREATE POLICY "Users can update own profile"
        ON profiles FOR UPDATE
        USING (auth.uid() = id);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- 7. Auto-create profile on user signup via trigger
CREATE OR REPLACE FUNCTION public.handle_new_user()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO public.profiles (id, nickname, full_name, created_at, updated_at)
    VALUES (
        NEW.id,
        COALESCE(NEW.raw_user_meta_data ->> 'nickname', split_part(NEW.email, '@', 1)),
        COALESCE(NEW.raw_user_meta_data ->> 'full_name', ''),
        NOW(),
        NOW()
    );
    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

DROP TRIGGER IF EXISTS on_auth_user_created ON auth.users;
CREATE TRIGGER on_auth_user_created
    AFTER INSERT ON auth.users
    FOR EACH ROW
    EXECUTE FUNCTION public.handle_new_user();
