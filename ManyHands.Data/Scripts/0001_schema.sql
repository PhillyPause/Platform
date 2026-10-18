-- The ManyHands schema.
--
-- This is the whole database, as it is today. Not a changelog: read `git log`
-- for how it got here. Until there is a deployed database with real volunteer
-- data in it, edit this file directly and recreate -- nothing is deployed, so
-- there is nothing to preserve and nothing to upgrade.
--
-- The day ManyHands is deployed for real, this file freezes and changes start
-- arriving as 0002_*.sql, 0003_*.sql and so on. DbUp already works that way;
-- nothing needs rewiring.
--
-- One organisation, PhillyPause, so there is no tenant column anywhere and no
-- row-level security. Who may read what is decided in the application, by the
-- signed-in volunteer's role (Docs/MANYHANDS.md).

CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;


-- ==========================================================================
-- People, and the things they turn up to
-- ==========================================================================

CREATE TABLE volunteers (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    display_name text NOT NULL,
    -- Null for someone who came in through Discord alone, and after deletion.
    email text,
    discord_user_id bigint,
    -- VolunteerRole: 0 member, 1 organizer, 2 admin.
    role integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    email_verified_at timestamp with time zone,
    email_opt_in boolean DEFAULT true NOT NULL,
    unsubscribed_at timestamp with time zone,
    -- Separate secrets: revoking a leaked calendar feed must not also break
    -- the person's unsubscribe links.
    comms_key uuid DEFAULT gen_random_uuid() NOT NULL,
    calendar_key uuid DEFAULT gen_random_uuid() NOT NULL,
    -- Deletion anonymises the row rather than removing it, so attendance
    -- counts stay true with nobody's name on them.
    is_deleted boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone,
    CONSTRAINT volunteers_pkey PRIMARY KEY (id),
    CONSTRAINT volunteers_discord_user_id_key UNIQUE (discord_user_id),
    CONSTRAINT volunteers_role_check CHECK (role BETWEEN 0 AND 2)
);

CREATE UNIQUE INDEX ux_volunteers_email_lower ON volunteers (lower(email)) WHERE email IS NOT NULL;
CREATE UNIQUE INDEX ux_volunteers_calendar_key ON volunteers (calendar_key);
CREATE UNIQUE INDEX ux_volunteers_comms_key ON volunteers (comms_key);

-- The interest vocabulary: volunteers pick from it and activities are tagged
-- from it, which is what makes "tell the people who like tabling" a query.
CREATE TABLE activity_types (
    id smallint NOT NULL,
    slug text NOT NULL,
    label text NOT NULL,
    description text,
    sort_order smallint DEFAULT 100 NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    CONSTRAINT activity_types_pkey PRIMARY KEY (id),
    CONSTRAINT activity_types_slug_key UNIQUE (slug),
    CONSTRAINT activity_types_slug_check CHECK (slug ~ '^[a-z0-9][a-z0-9-]*$')
);

-- A specific thing a volunteer signs up for. It carries its own time, place
-- and capacity; there is no slot object to pre-register into.
CREATE TABLE activities (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    title text NOT NULL,
    description text,
    -- ActivityKind: 0 one-time, 1 ongoing.
    kind integer DEFAULT 0 NOT NULL,
    location text,
    meeting_url text,
    -- Null for ongoing work people do whenever they can.
    starts_at timestamp with time zone,
    ends_at timestamp with time zone,
    -- Null means no limit, which is the usual case outdoors.
    capacity integer,
    is_published boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT activities_pkey PRIMARY KEY (id),
    CONSTRAINT activities_ends_after_start CHECK (ends_at IS NULL OR starts_at IS NULL OR ends_at > starts_at),
    CONSTRAINT activities_capacity_check CHECK (capacity IS NULL OR capacity > 0)
);

CREATE INDEX ix_activities_starts_at ON activities (starts_at) WHERE is_published AND starts_at IS NOT NULL;

CREATE TABLE activity_tags (
    activity_id uuid NOT NULL REFERENCES activities (id) ON DELETE CASCADE,
    activity_type_id smallint NOT NULL REFERENCES activity_types (id),
    CONSTRAINT activity_tags_pkey PRIMARY KEY (activity_id, activity_type_id)
);

CREATE INDEX ix_activity_tags_type ON activity_tags (activity_type_id);

CREATE TABLE volunteer_interests (
    volunteer_id uuid NOT NULL REFERENCES volunteers (id) ON DELETE CASCADE,
    activity_type_id smallint NOT NULL REFERENCES activity_types (id),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT volunteer_interests_pkey PRIMARY KEY (volunteer_id, activity_type_id)
);

CREATE TABLE signups (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    activity_id uuid NOT NULL REFERENCES activities (id) ON DELETE CASCADE,
    volunteer_id uuid NOT NULL REFERENCES volunteers (id),
    -- SignupStatus: 0 confirmed, 1 waitlisted, 2 cancelled.
    status integer DEFAULT 0 NOT NULL,
    -- SignupSource: 0 web, 1 Discord, 2 organizer, 3 import.
    source integer DEFAULT 0 NOT NULL,
    note text,
    checked_in_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT signups_pkey PRIMARY KEY (id),
    CONSTRAINT ux_signups_activity_volunteer UNIQUE (activity_id, volunteer_id)
);

CREATE INDEX ix_signups_volunteer ON signups (volunteer_id);


-- ==========================================================================
-- Getting in, and staying signed in
-- ==========================================================================

-- Only the hash of a sign-in link is stored, so a read of this table cannot
-- be turned into a sign-in.
CREATE TABLE auth_tokens (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    volunteer_id uuid NOT NULL REFERENCES volunteers (id) ON DELETE CASCADE,
    token_hash bytea NOT NULL,
    purpose text DEFAULT 'magic-link' NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    consumed_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT auth_tokens_pkey PRIMARY KEY (id),
    CONSTRAINT auth_tokens_token_hash_key UNIQUE (token_hash)
);

CREATE INDEX ix_auth_tokens_volunteer ON auth_tokens (volunteer_id, created_at DESC);

CREATE TABLE invites (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    email text NOT NULL,
    note text,
    invited_by uuid REFERENCES volunteers (id) ON DELETE SET NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    expires_at timestamp with time zone DEFAULT (now() + interval '14 days') NOT NULL,
    sent_at timestamp with time zone,
    accepted_at timestamp with time zone,
    accepted_by uuid REFERENCES volunteers (id) ON DELETE SET NULL,
    revoked_at timestamp with time zone,
    CONSTRAINT invites_pkey PRIMARY KEY (id),
    CONSTRAINT ck_invites_email_shape CHECK (position('@' IN email) > 1)
);

CREATE INDEX ix_invites_created ON invites (created_at DESC);
CREATE UNIQUE INDEX ux_invites_open ON invites (lower(email)) WHERE accepted_at IS NULL AND revoked_at IS NULL;

-- Kept apart from invites so that listing invites never reads a live link.
CREATE TABLE invite_tokens (
    invite_id uuid NOT NULL REFERENCES invites (id) ON DELETE CASCADE,
    token_hash bytea NOT NULL,
    CONSTRAINT invite_tokens_pkey PRIMARY KEY (invite_id),
    CONSTRAINT invite_tokens_token_hash_key UNIQUE (token_hash)
);


-- The keys that sign the sign-in cookie and the forms' antiforgery tokens.
-- Kept here so a deploy, which replaces the container, does not sign every
-- volunteer out. ASP.NET Data Protection reads and writes it; nothing else.
CREATE TABLE data_protection_keys (
    friendly_name text NOT NULL,
    xml text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT data_protection_keys_pkey PRIMARY KEY (friendly_name)
);


-- ==========================================================================
-- Telling people things
-- ==========================================================================

-- Every message leaves through here. Delivery is at least once, so every
-- handler must survive a rerun of the same row.
CREATE TABLE outbox (
    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
    -- Dispatcher discriminator, e.g. signup.confirmed, activity.reminder.
    -- Renaming one means migrating the stored rows too.
    kind text NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    processed_at timestamp with time zone,
    attempts integer DEFAULT 0 NOT NULL,
    last_error text,
    next_attempt_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT outbox_pkey PRIMARY KEY (id)
);

CREATE INDEX ix_outbox_unprocessed ON outbox (next_attempt_at, id) WHERE processed_at IS NULL;

-- Volunteers' own transactional mail: confirmations, reminders, sign-in
-- links. Personal correspondence, so no page shows a body; outreach mail is
-- crm_emails, which the team does see.
CREATE TABLE email_messages (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    volunteer_id uuid REFERENCES volunteers (id) ON DELETE SET NULL,
    to_address text NOT NULL,
    subject text NOT NULL,
    body_text text NOT NULL,
    kind text NOT NULL,
    -- EmailStatus: 0 queued, 1 sent, 2 failed, 3 simulated, 4 suppressed.
    status integer DEFAULT 0 NOT NULL,
    provider_message_id text,
    error text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    sent_at timestamp with time zone,
    CONSTRAINT email_messages_pkey PRIMARY KEY (id)
);

CREATE INDEX ix_email_messages_volunteer ON email_messages (volunteer_id, created_at DESC);
CREATE INDEX ix_email_messages_recipient ON email_messages (lower(to_address), created_at DESC);

-- Where an activity has been announced outside ManyHands. Discord is the one
-- target; the column stays so a redelivered announcement finds its own row.
CREATE TABLE external_publications (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    activity_id uuid NOT NULL REFERENCES activities (id) ON DELETE CASCADE,
    target text NOT NULL,
    external_id text,
    external_url text,
    thread_id bigint,
    synced_at timestamp with time zone,
    last_error text,
    CONSTRAINT external_publications_pkey PRIMARY KEY (id),
    CONSTRAINT external_publications_activity_id_target_key UNIQUE (activity_id, target)
);

-- Who has already been told about an activity, per channel, so nobody hears
-- twice about the same thing.
CREATE TABLE activity_pings (
    activity_id uuid NOT NULL REFERENCES activities (id) ON DELETE CASCADE,
    volunteer_id uuid NOT NULL REFERENCES volunteers (id) ON DELETE CASCADE,
    channel text NOT NULL,
    claimed_at timestamp with time zone DEFAULT now() NOT NULL,
    delivered boolean DEFAULT false NOT NULL,
    CONSTRAINT activity_pings_pkey PRIMARY KEY (activity_id, volunteer_id, channel)
);

CREATE INDEX ix_activity_pings_volunteer ON activity_pings (volunteer_id);

-- Discord roles the bot granted itself. Only these does it ever take back:
-- listing a server's members needs a privileged intent it does not ask for.
CREATE TABLE discord_role_grants (
    volunteer_id uuid NOT NULL REFERENCES volunteers (id) ON DELETE CASCADE,
    discord_user_id bigint NOT NULL,
    role_id bigint NOT NULL,
    granted_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT discord_role_grants_pkey PRIMARY KEY (volunteer_id)
);


-- ==========================================================================
-- Lobbying CRM
-- ==========================================================================

CREATE TABLE crm_organizations (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    kind integer DEFAULT 0 NOT NULL,
    name text NOT NULL,
    description text,
    website text,
    address text,
    state text,
    district text,
    -- e.g. a federal official's bioguide id, when imported from the directory.
    external_ref text,
    created_by uuid REFERENCES volunteers (id),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone,
    CONSTRAINT crm_organizations_pkey PRIMARY KEY (id),
    CONSTRAINT crm_organizations_state_check CHECK (state IS NULL OR state ~ '^[A-Z]{2}$')
);

CREATE UNIQUE INDEX ux_crm_orgs_external ON crm_organizations (external_ref) WHERE external_ref IS NOT NULL;

CREATE TABLE crm_contacts (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    organization_id uuid REFERENCES crm_organizations (id) ON DELETE SET NULL,
    full_name text NOT NULL,
    role_title text,
    email text,
    phone text,
    notes text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone,
    CONSTRAINT crm_contacts_pkey PRIMARY KEY (id)
);

CREATE INDEX ix_crm_contacts_org ON crm_contacts (organization_id) WHERE NOT is_deleted;

CREATE TABLE crm_engagements (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    organization_id uuid REFERENCES crm_organizations (id) ON DELETE SET NULL,
    title text NOT NULL,
    status integer DEFAULT 0 NOT NULL,
    activity_category integer,
    owner_id uuid REFERENCES volunteers (id),
    scheduled_at timestamp with time zone,
    location text,
    meeting_url text,
    summary text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone,
    CONSTRAINT crm_engagements_pkey PRIMARY KEY (id)
);

CREATE INDEX ix_crm_engagements_status ON crm_engagements (status) WHERE NOT is_deleted;
CREATE INDEX ix_crm_engagements_org ON crm_engagements (organization_id) WHERE NOT is_deleted;

-- Exactly one of a volunteer or a contact.
CREATE TABLE crm_engagement_participants (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    engagement_id uuid NOT NULL REFERENCES crm_engagements (id) ON DELETE CASCADE,
    volunteer_id uuid REFERENCES volunteers (id) ON DELETE CASCADE,
    contact_id uuid REFERENCES crm_contacts (id) ON DELETE CASCADE,
    CONSTRAINT crm_engagement_participants_pkey PRIMARY KEY (id),
    CONSTRAINT crm_engagement_participants_check CHECK ((volunteer_id IS NULL) <> (contact_id IS NULL))
);

CREATE UNIQUE INDEX ux_crm_participants_contact ON crm_engagement_participants (engagement_id, contact_id) WHERE contact_id IS NOT NULL;
CREATE UNIQUE INDEX ux_crm_participants_volunteer ON crm_engagement_participants (engagement_id, volunteer_id) WHERE volunteer_id IS NOT NULL;

CREATE TABLE crm_interactions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    organization_id uuid REFERENCES crm_organizations (id) ON DELETE CASCADE,
    contact_id uuid REFERENCES crm_contacts (id) ON DELETE CASCADE,
    engagement_id uuid REFERENCES crm_engagements (id) ON DELETE CASCADE,
    kind integer DEFAULT 0 NOT NULL,
    occurred_at timestamp with time zone DEFAULT now() NOT NULL,
    body text NOT NULL,
    logged_by uuid REFERENCES volunteers (id),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT crm_interactions_pkey PRIMARY KEY (id)
);

CREATE INDEX ix_crm_interactions_contact ON crm_interactions (contact_id, occurred_at DESC);
CREATE INDEX ix_crm_interactions_engagement ON crm_interactions (engagement_id, occurred_at DESC);
CREATE INDEX ix_crm_interactions_org ON crm_interactions (organization_id, occurred_at DESC);

CREATE TABLE crm_tasks (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    engagement_id uuid REFERENCES crm_engagements (id) ON DELETE CASCADE,
    organization_id uuid REFERENCES crm_organizations (id) ON DELETE CASCADE,
    title text NOT NULL,
    notes text,
    status integer DEFAULT 0 NOT NULL,
    due_on date,
    assigned_to uuid REFERENCES volunteers (id),
    completed_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone,
    CONSTRAINT crm_tasks_pkey PRIMARY KEY (id)
);

CREATE INDEX ix_crm_tasks_assignee ON crm_tasks (assigned_to, status) WHERE NOT is_deleted;
CREATE INDEX ix_crm_tasks_status ON crm_tasks (status) WHERE NOT is_deleted;

-- Outreach mail, sent and received inside ManyHands and visible to the team.
-- Deliberately not email_messages, which is volunteers' private mail.
CREATE TABLE crm_emails (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    -- 0 outbound, 1 inbound.
    direction integer DEFAULT 0 NOT NULL,
    organization_id uuid REFERENCES crm_organizations (id) ON DELETE SET NULL,
    contact_id uuid REFERENCES crm_contacts (id) ON DELETE SET NULL,
    engagement_id uuid REFERENCES crm_engagements (id) ON DELETE SET NULL,
    from_address text NOT NULL,
    to_address text NOT NULL,
    subject text NOT NULL,
    body_text text NOT NULL,
    message_id text,
    in_reply_to text,
    conversation_id uuid NOT NULL,
    status integer DEFAULT 0 NOT NULL,
    provider_message_id text,
    error text,
    sent_by uuid REFERENCES volunteers (id),
    -- Mail from an address we do not recognise is kept, and queued here.
    needs_triage boolean DEFAULT false NOT NULL,
    occurred_at timestamp with time zone DEFAULT now() NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT crm_emails_pkey PRIMARY KEY (id)
);

CREATE INDEX ix_crm_emails_occurred ON crm_emails (occurred_at DESC);
CREATE INDEX ix_crm_emails_contact ON crm_emails (contact_id, occurred_at DESC);
CREATE INDEX ix_crm_emails_conversation ON crm_emails (conversation_id, occurred_at);
CREATE INDEX ix_crm_emails_org ON crm_emails (organization_id, occurred_at DESC);
CREATE INDEX ix_crm_emails_triage ON crm_emails (occurred_at) WHERE needs_triage;
CREATE UNIQUE INDEX ux_crm_emails_message_id ON crm_emails (message_id) WHERE message_id IS NOT NULL;

CREATE TABLE crm_email_templates (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name text NOT NULL,
    subject text NOT NULL,
    body_text text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone,
    CONSTRAINT crm_email_templates_pkey PRIMARY KEY (id)
);


-- ==========================================================================
-- The federal officials directory, refreshed from a public dataset
-- ==========================================================================

CREATE TABLE federal_officials (
    bioguide_id text NOT NULL,
    full_name text NOT NULL,
    chamber text NOT NULL,
    state text NOT NULL,
    district text,
    party text,
    term_ends_on date,
    website text,
    phone text,
    office text,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT federal_officials_pkey PRIMARY KEY (bioguide_id),
    CONSTRAINT federal_officials_chamber_check CHECK (chamber IN ('house', 'senate')),
    CONSTRAINT federal_officials_state_check CHECK (state ~ '^[A-Z]{2}$')
);

CREATE INDEX ix_federal_officials_state ON federal_officials (state, chamber);

CREATE TABLE sync_runs (
    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
    source text NOT NULL,
    started_at timestamp with time zone DEFAULT now() NOT NULL,
    finished_at timestamp with time zone,
    records integer,
    error text,
    CONSTRAINT sync_runs_pkey PRIMARY KEY (id)
);


-- ==========================================================================
-- Reference data
-- ==========================================================================

-- Not demo data: the app is wrong without it.
INSERT INTO activity_types (id, slug, label, description, sort_order) VALUES
  (1,  'tabling',        'Tabling',               'Staffing an info table at events or on the street.',   10),
  (2,  'flyering',       'Flyering',              'Handing out flyers and talking to passers-by.',         20),
  (3,  'calling',        'Calling Congress',      'Phone calls to congressional offices.',                 30),
  (4,  'letter-writing', 'Letter & email writing','Writing to representatives and local press.',           40),
  (5,  'lobby-meetings', 'Lobby meetings',        'Attending meetings with congressional offices.',        50),
  (6,  'event-support',  'Event support',         'Setup, logistics, greeting, and teardown.',             60),
  (7,  'protests',       'Protests & rallies',    'Showing up for demonstrations.',                        70),
  (8,  'social-media',   'Social media',          'Posting, amplifying, and community management.',        80),
  (9,  'research',       'Research & writing',    'Background research, briefs, and talking points.',      90),
  (10, 'tech-ops',       'Tech & ops',            'Website, data, tooling, and back-office help.',        100),
  (11, 'design',         'Design & media',        'Graphics, photography, and video.',                    110),
  (12, 'outreach',       'Community outreach',    'Partner orgs, campus groups, and coalition building.', 120);
