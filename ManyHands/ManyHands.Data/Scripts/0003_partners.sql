-- Partner outreach: the organisations we ask to share PhillyPause with their
-- members, worked from the Outreach Kit (MANYHANDS.md, "Partner outreach").
-- Adds columns and one table and changes no row.
--
-- An organization is a partner prospect once outreach_status is set; offices
-- and venues that are not leave it null and nothing about them changes.

ALTER TABLE crm_organizations
    ADD COLUMN priority char(1)
        CONSTRAINT crm_organizations_priority_check CHECK (priority IN ('A', 'B', 'C')),
    ADD COLUMN outreach_status integer,
    ADD COLUMN audience integer,
    ADD COLUMN ask_rung integer,
    ADD COLUMN category text,
    ADD COLUMN tracking_code text
        CONSTRAINT crm_organizations_tracking_code_check CHECK (tracking_code ~ '^[a-z0-9-]{1,40}$'),
    ADD COLUMN contact_channel text,
    ADD COLUMN found_via text,
    ADD COLUMN why_they_fit text,
    ADD COLUMN first_contacted_at timestamp with time zone,
    ADD COLUMN followed_up_at timestamp with time zone,
    ADD COLUMN replied_at timestamp with time zone;

-- One live organization per code: a code says which partner brought someone.
CREATE UNIQUE INDEX crm_organizations_tracking_code_key
    ON crm_organizations (tracking_code) WHERE tracking_code IS NOT NULL AND NOT is_deleted;

CREATE INDEX crm_organizations_outreach_idx
    ON crm_organizations (outreach_status) WHERE outreach_status IS NOT NULL AND NOT is_deleted;

-- How organisations know each other: "Pen & Pencil Club hosts meetups of IRE
-- Philadelphia". Either end may be something that is not an organization
-- here (a hearing, a coalition), kept by name; at least one end is one.
CREATE TABLE crm_organization_links (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    from_organization_id uuid REFERENCES crm_organizations (id) ON DELETE CASCADE,
    from_name text,
    connection text NOT NULL,
    to_organization_id uuid REFERENCES crm_organizations (id) ON DELETE CASCADE,
    to_name text,
    source_url text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone,
    CONSTRAINT crm_organization_links_pkey PRIMARY KEY (id),
    CONSTRAINT crm_organization_links_from_check CHECK ((from_organization_id IS NULL) <> (from_name IS NULL)),
    CONSTRAINT crm_organization_links_to_check CHECK ((to_organization_id IS NULL) <> (to_name IS NULL)),
    CONSTRAINT crm_organization_links_one_org_check
        CHECK (from_organization_id IS NOT NULL OR to_organization_id IS NOT NULL)
);

CREATE INDEX crm_organization_links_from_idx ON crm_organization_links (from_organization_id) WHERE NOT is_deleted;
CREATE INDEX crm_organization_links_to_idx ON crm_organization_links (to_organization_id) WHERE NOT is_deleted;
