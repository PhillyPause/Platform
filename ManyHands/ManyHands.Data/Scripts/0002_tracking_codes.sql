-- Where people come from: the code on the link that brought them
-- (phillypause.org/?src=newsguild). Adds two columns and changes no row.
--
-- volunteers.source is the first touch, written when the row is created and
-- never after. signups.referral is the code on that one signup; the existing
-- signups.source is the channel (web, Discord, organizer) and stays as it is.

ALTER TABLE volunteers
    ADD COLUMN source text
        CONSTRAINT volunteers_source_check CHECK (source ~ '^[a-z0-9-]{1,40}$');

ALTER TABLE signups
    ADD COLUMN referral text
        CONSTRAINT signups_referral_check CHECK (referral ~ '^[a-z0-9-]{1,40}$');

CREATE INDEX volunteers_source_idx ON volunteers (source) WHERE source IS NOT NULL;
