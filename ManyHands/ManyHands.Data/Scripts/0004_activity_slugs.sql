-- Readable addresses for activities: phillypause.org/events/philly-says-pause
-- instead of /volunteer/activity/<uuid>. Adds two columns and fills them in;
-- nothing else on any row changes.
--
-- activities.slug is the address. It follows the title while the activity is
-- a draft, may be set by hand then, and is frozen from the first publish on:
-- by then it is in emails, Discord posts and on posters. When a title's slug
-- is taken, the activity's date (Philadelphia's) is added, and failing that
-- the start of its id.
--
-- activities.published_at is when it was first published: the freeze.

ALTER TABLE activities ADD COLUMN slug text;
ALTER TABLE activities ADD COLUMN published_at timestamp with time zone;

-- Lower-case letters and digits, runs of anything else become one dash, at
-- most 60 characters, never empty. ActivitySlug.From in ManyHands.Core
-- follows the same rule, so a page can show the address before saving.
CREATE FUNCTION activity_slug_base(source text) RETURNS text
LANGUAGE sql IMMUTABLE AS
$$
    SELECT COALESCE(
        NULLIF(trim(both '-' from left(regexp_replace(lower(coalesce(source, '')), '[^a-z0-9]+', '-', 'g'), 60)), ''),
        'event')
$$;

CREATE FUNCTION activity_unique_slug(base text, starts_at timestamp with time zone, activity_id uuid) RETURNS text
LANGUAGE plpgsql STABLE AS
$$
DECLARE
    candidate text := base;
BEGIN
    IF EXISTS (SELECT 1 FROM activities WHERE slug = candidate AND id <> activity_id) AND starts_at IS NOT NULL THEN
        candidate := base || '-' || to_char(starts_at AT TIME ZONE 'America/New_York', 'YYYY-MM-DD');
    END IF;
    IF EXISTS (SELECT 1 FROM activities WHERE slug = candidate AND id <> activity_id) THEN
        candidate := base || '-' || left(replace(activity_id::text, '-', ''), 8);
    END IF;
    RETURN candidate;
END
$$;

-- What is already published counts as published since it was created.
UPDATE activities SET published_at = created_at WHERE is_published;

-- Oldest first, so an older activity keeps the plain title.
DO $$
DECLARE
    r record;
BEGIN
    FOR r IN SELECT id, title, starts_at FROM activities ORDER BY created_at, id LOOP
        UPDATE activities
           SET slug = activity_unique_slug(activity_slug_base(r.title), r.starts_at, r.id)
         WHERE id = r.id;
    END LOOP;
END
$$;

ALTER TABLE activities ALTER COLUMN slug SET NOT NULL;
ALTER TABLE activities ADD CONSTRAINT activities_slug_check CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$');
CREATE UNIQUE INDEX activities_slug_key ON activities (slug);

-- Every insert and update goes through this, so no writer can forget it:
-- a new row gets a slug from its title (or the one it was given); a draft's
-- follows its title, or takes the one an organizer typed; a published one's
-- never changes.
CREATE FUNCTION activities_set_slug() RETURNS trigger
LANGUAGE plpgsql AS
$$
BEGIN
    IF TG_OP = 'UPDATE' THEN
        IF OLD.published_at IS NOT NULL THEN
            NEW.slug := OLD.slug;
            RETURN NEW;
        END IF;
        IF NEW.slug IS NOT DISTINCT FROM OLD.slug THEN
            IF NEW.title IS NOT DISTINCT FROM OLD.title THEN
                RETURN NEW;
            END IF;
            NEW.slug := NULL;
        END IF;
    END IF;

    NEW.slug := activity_unique_slug(activity_slug_base(COALESCE(NULLIF(NEW.slug, ''), NEW.title)), NEW.starts_at, NEW.id);
    RETURN NEW;
END
$$;

CREATE TRIGGER activities_set_slug
    BEFORE INSERT OR UPDATE ON activities
    FOR EACH ROW EXECUTE FUNCTION activities_set_slug();
