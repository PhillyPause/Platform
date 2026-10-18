-- Fictional data for local development. Runs only when ManyHands:Seed is true,
-- which nothing sets in production. Every address is on example.test, which can
-- never receive mail, and every name is invented.
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

INSERT INTO activities (id, title, description, kind, location, starts_at, ends_at, capacity, is_published) VALUES
  ('00000000-0000-0000-0000-00000000b001', 'Saturday tabling at Clark Park',
   E'Two of us at a folding table by the farmers market, handing out cards and talking to whoever stops.\n\nNo experience needed: we pair you with someone who has done it before.',
   0, 'Clark Park, 43rd and Baltimore', date_trunc('hour', now()) + interval '3 days 14 hours', date_trunc('hour', now()) + interval '3 days 16 hours', 4, true),
  ('00000000-0000-0000-0000-00000000b002', 'Call your representative',
   'Fifteen minutes with a script, whenever suits you. We will walk you through it first if you would rather.',
   1, NULL, NULL, NULL, NULL, true),
  ('00000000-0000-0000-0000-00000000b003', 'Screening: set-up crew',
   'Chairs, the projector, and the sign-in table, an hour before the doors open.',
   0, 'The Pen and Pencil Club, 1522 Latimer St', date_trunc('hour', now()) + interval '9 days 18 hours', date_trunc('hour', now()) + interval '9 days 19 hours', 1, true),
  ('00000000-0000-0000-0000-00000000b004', 'Draft: campus flyering',
   'Not published yet.', 0, 'Penn campus', date_trunc('hour', now()) + interval '14 days 12 hours', date_trunc('hour', now()) + interval '14 days 14 hours', NULL, false);

INSERT INTO activity_tags (activity_id, activity_type_id) VALUES
  ('00000000-0000-0000-0000-00000000b001', 1),
  ('00000000-0000-0000-0000-00000000b001', 12),
  ('00000000-0000-0000-0000-00000000b002', 3),
  ('00000000-0000-0000-0000-00000000b003', 6),
  ('00000000-0000-0000-0000-00000000b004', 2);

-- The set-up crew is full, so the waitlist has something to show.
INSERT INTO signups (activity_id, volunteer_id, status) VALUES
  ('00000000-0000-0000-0000-00000000b003', '00000000-0000-0000-0000-00000000a003', 0),
  ('00000000-0000-0000-0000-00000000b001', '00000000-0000-0000-0000-00000000a002', 0);
