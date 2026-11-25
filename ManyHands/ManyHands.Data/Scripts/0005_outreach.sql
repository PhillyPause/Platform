-- Outreach: a record of people contacting their elected officials from the
-- action centre (/act), so a member can see what they have done and
-- organizers can see who is being reached, by whom and how.
--
-- A new script because 0001 to 0004 have run in production and are frozen.
--
-- The official is named by the action centre's key (the Markdown file in
-- Content/_officials) and by name as it was then, so a record still reads
-- right after the directory moves on. Nothing here holds the message that
-- was sent, an address, or a phone number: only that it happened.

CREATE TABLE outreach_actions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    -- Null for someone who was not signed in: counted, but nobody's.
    volunteer_id uuid REFERENCES volunteers (id) ON DELETE SET NULL,
    official_key text NOT NULL,
    official_name text NOT NULL,
    -- OutreachChannel: 0 email, 1 call, 2 contact form, 3 letter, 4 meeting.
    channel integer NOT NULL,
    -- OutreachOutcome: 0 sent, 1 spoke to someone, 2 left a voicemail,
    -- 3 no answer. Null when not said.
    outcome integer,
    -- What the person chose to tell us, in a sentence. Cleared on deletion.
    note text,
    occurred_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT outreach_actions_pkey PRIMARY KEY (id),
    CONSTRAINT outreach_actions_channel_check CHECK (channel BETWEEN 0 AND 4),
    CONSTRAINT outreach_actions_outcome_check CHECK (outcome IS NULL OR outcome BETWEEN 0 AND 3),
    CONSTRAINT outreach_actions_note_length CHECK (note IS NULL OR length(note) <= 500)
);

CREATE INDEX ix_outreach_actions_occurred_at ON outreach_actions (occurred_at DESC);
CREATE INDEX ix_outreach_actions_volunteer ON outreach_actions (volunteer_id, occurred_at DESC) WHERE volunteer_id IS NOT NULL;
CREATE INDEX ix_outreach_actions_official ON outreach_actions (official_key, occurred_at DESC);
