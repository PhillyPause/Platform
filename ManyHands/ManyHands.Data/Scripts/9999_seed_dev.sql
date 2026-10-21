-- Fictional data for local development. Runs only when ManyHands:Seed is true,
-- and only in Development: set anywhere else, the site refuses to start. Every
-- address is on example.test, which can never receive mail, and every name is
-- invented.
--
-- No activities. An invented event on the Events list reads as a real one, so
-- every event there is one an organizer made.
--
-- Numbered last so it always runs after the schema, whatever migrations arrive
-- between.

INSERT INTO volunteers (id, display_name, email, role, email_verified_at) VALUES
  ('00000000-0000-0000-0000-00000000a001', 'Ada Organizer', 'organizer@example.test', 2, now()),
  ('00000000-0000-0000-0000-00000000a002', 'Ben Volunteer', 'ben@example.test', 0, now()),
  ('00000000-0000-0000-0000-00000000a003', 'Cai Volunteer', 'cai@example.test', 0, now());

INSERT INTO volunteer_interests (volunteer_id, activity_type_id) VALUES
  ('00000000-0000-0000-0000-00000000a002', 1),
  ('00000000-0000-0000-0000-00000000a002', 2),
  ('00000000-0000-0000-0000-00000000a003', 3);

-- The CRM: one congressional office with a staffer, an effort under way, a
-- task, and a short exchange of mail, plus a stranger's message to triage.
INSERT INTO crm_organizations (id, kind, name, description, state, district, created_by) VALUES
  ('00000000-0000-0000-0000-00000000c001', 0, 'Office of Robin Rivera', 'Democrat · Representative, PA-3', 'PA', '03',
   '00000000-0000-0000-0000-00000000a001'),
  ('00000000-0000-0000-0000-00000000c002', 3, 'The Pen and Pencil Club', 'Screening venue.', 'PA', NULL,
   '00000000-0000-0000-0000-00000000a001');

INSERT INTO crm_contacts (id, organization_id, full_name, role_title, email, phone) VALUES
  ('00000000-0000-0000-0000-00000000c101', '00000000-0000-0000-0000-00000000c001', 'Jamie Staffer', 'Scheduler',
   'jamie@example.test', '202-555-0100');

INSERT INTO crm_engagements (id, organization_id, title, status, activity_category, owner_id, scheduled_at, location) VALUES
  ('00000000-0000-0000-0000-00000000c201', '00000000-0000-0000-0000-00000000c001', 'District office meeting about AI safety',
   2, 0, '00000000-0000-0000-0000-00000000a001', date_trunc('hour', now()) + interval '12 days 15 hours', 'District office');

INSERT INTO crm_tasks (engagement_id, title, due_on, assigned_to) VALUES
  ('00000000-0000-0000-0000-00000000c201', 'Send the one-page brief ahead of the meeting', current_date + 5,
   '00000000-0000-0000-0000-00000000a001');

INSERT INTO crm_interactions (organization_id, contact_id, engagement_id, kind, occurred_at, body, logged_by) VALUES
  ('00000000-0000-0000-0000-00000000c001', '00000000-0000-0000-0000-00000000c101', '00000000-0000-0000-0000-00000000c201',
   1, now() - interval '3 days', 'Called the scheduler; they suggested the district office.', '00000000-0000-0000-0000-00000000a001');

INSERT INTO crm_emails (direction, organization_id, contact_id, engagement_id, from_address, to_address, subject, body_text,
                        message_id, conversation_id, status, sent_by, occurred_at, needs_triage) VALUES
  (0, '00000000-0000-0000-0000-00000000c001', '00000000-0000-0000-0000-00000000c101', '00000000-0000-0000-0000-00000000c201',
   'outreach@example.test', 'jamie@example.test', 'Meeting request: AI safety',
   E'Dear Jamie,\n\nWe are constituents in PA-3 and would value twenty minutes with the Representative.',
   '<seed-1@example.test>', '00000000-0000-0000-0000-00000000c301', 3, '00000000-0000-0000-0000-00000000a001',
   now() - interval '2 days', false),
  (1, '00000000-0000-0000-0000-00000000c001', '00000000-0000-0000-0000-00000000c101', NULL,
   'Jamie Staffer <jamie@example.test>', 'outreach@example.test', 'Re: Meeting request: AI safety',
   'Happy to. Does the district office work?', '<seed-2@example.test>', '00000000-0000-0000-0000-00000000c301', 1, NULL,
   now() - interval '1 day', false),
  (1, NULL, NULL, NULL, 'Sam Organizer <sam@example.test>', 'outreach@example.test', 'Joint event this spring?',
   'We run a campus group and would love to co-host something.', '<seed-3@example.test>',
   '00000000-0000-0000-0000-00000000c302', 1, NULL, now() - interval '5 hours', true);

INSERT INTO crm_email_templates (name, subject, body_text) VALUES
  ('Meeting request', 'Meeting request: AI safety',
   E'Dear ___,\n\nWe are constituents in ___ and would value twenty minutes with the ___ about AI safety.\n\nThank you,');

-- Two invented members of Congress, so the directory has something to show
-- before its first weekly refresh.
INSERT INTO federal_officials (bioguide_id, full_name, chamber, state, district, party, website, phone, office) VALUES
  ('X000001', 'Robin Rivera', 'house', 'PA', '03', 'Democrat', 'https://example.test/rivera', '202-555-0101', '123 Cannon House Office Building'),
  ('X000002', 'Dana Cole', 'senate', 'PA', NULL, 'Republican', 'https://example.test/cole', '202-555-0102', '456 Russell Senate Office Building');
