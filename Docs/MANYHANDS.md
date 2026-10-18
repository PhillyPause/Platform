# ManyHands

PhillyPause's volunteer system: what needs doing, who said they would do it,
and the people outside PhillyPause we are working on. It lives in this
solution beside the website, is served by the same host, and is **off unless
`ManyHands:Enabled` is set**.

It serves **one organisation, PhillyPause, and nothing else**. It began as a
multi-chapter platform for PauseAI US in its own repo
(`PhillyPause/ManyHands`, now archived). What came across is the subset
PhillyPause uses, with every trace of the multi-chapter design taken out.
Simplicity is the point: nothing here exists to serve a second organisation.

## 1. What it does

| Area | What a person can do |
|---|---|
| Activities | Browse what is coming up without an account; filter by type |
| Signup | Sign up with a name, an email and the kinds of thing they like doing; capacity and a waitlist, and a freed place goes to the next person in line |
| Sign-in | Passwordless: a link by email |
| Mail | Confirmation, a reminder the day before, "you are in" off the waitlist, a welcome, one-click unsubscribe |
| Self-service | See and cancel their signups, change interests, export everything held about them, delete themselves |
| Calendar | An ICS feed of activities, and a private one of their own signups |
| Organizing | Create, edit and publish activities; the roster; QR check-in at the door; invite people by email; change who is an organizer; export participation |
| CRM | Organizations, contacts, a timeline of interactions, an engagement pipeline, tasks |
| Outreach email | Compose, send, and receive replies inside ManyHands, threaded; unrecognised mail is kept for triage |
| Officials | The members of Congress, refreshed weekly from a public dataset, importable into the CRM |
| Discord | Announce on publish, a thread per activity, a DM on signup, slash commands, interest pings, role sync |

Not here, and not coming back: multiple chapters or a "national" tier,
row-level security, Luma publishing, web push, logged hours, the Blazor WASM
client and its HTTP API, Aspire, and a separate worker process.

What is built so far is in [section 7](#7-status).

## 2. Rules — settled, do not relitigate

- **One organisation.** No chapter, tenant or national concept anywhere: not a
  table, a column, a parameter or a word in the UI. `SchemaTests` fails if a
  tenant column or a row-level-security policy appears.
- **Activities, not shifts.** Volunteers state *interests* (activity types)
  and sign up for *activities*, which carry their own time, place and
  capacity. "Shift" and "opportunity" appear nowhere: not in the schema, the
  code or the copy.
- **Interests, not hours.** Participation is derived from signups and
  check-ins. There is no timesheet.
- **All outreach mail lives inside ManyHands**, inbound and outbound. Nobody's
  personal inbox is the system of record.
- **Static server rendering.** The pages are server-rendered Razor components
  and `EditForm`s, like the rest of the site. They call the data layer
  directly; there is no HTTP API and no WASM.
- **The site never needs ManyHands.** Off, nothing of it is registered and the
  site needs no database. On, the home page still never waits on the
  database: what it shows of ManyHands comes from an in-memory copy a worker
  keeps fresh, falling back to static content, so an outage cannot take the
  front page down.
- **Every message leaves through the outbox**, queued in the same transaction
  as the write that caused it. Delivery is at least once, so every handler
  must survive running twice. Renaming an outbox `kind` means migrating the
  stored rows too, or reminders re-send.
- **Signup capacity is serialised with `pg_advisory_xact_lock`** on the
  activity, taken by both signup and cancellation, so two people cannot take
  the last place and a freed place goes to exactly one person.
- **Secrets are never stored readable.** Sign-in and invite links are kept as
  hashes. Volunteers' own mail (`email_messages`) is shown on no page.
- **Seed data is fictional and opt-in**: a script whose name contains
  `_seed_` runs only when `ManyHands:Seed` is true, which nothing sets in
  production. `example.test` addresses and `555-01xx` numbers, never real
  people.

## 3. Shape

| Project | Holds |
|---|---|
| `ManyHands.Core` | Records, enums and results. No framework, no packages |
| `ManyHands.Data` | Dapper repositories, the DbUp migrations, mail, Discord, the federal dataset, the background workers |
| `ManyHands.Tests` | xunit, against real Postgres in a container |
| `Website.Portal` | The pages, under its own components, and the few endpoints a page cannot be: the inbound mail webhook, one-click unsubscribe, the ICS feeds |

`Website.Portal/Program.cs` calls `AddManyHands()` and `MigrateManyHands()`
only when the flag is on. The background workers are hosted services in the
same process: there is one instance, so there is nothing to coordinate.

## 4. Data

Postgres, one schema (`public`), defined by
[`ManyHands.Data/Scripts/0001_schema.sql`](../ManyHands.Data/Scripts/0001_schema.sql).

| Table | Holds |
|---|---|
| `volunteers` | Everyone, with their `role` (member, organizer, admin). Deletion anonymises the row |
| `activity_types` | The interest vocabulary. Reference data, inserted by the schema |
| `activities`, `activity_tags` | What needs doing, and which types each is |
| `volunteer_interests` | What each person likes doing |
| `signups` | Who said they would come, their status, and whether they checked in |
| `auth_tokens` | Sign-in link hashes |
| `invites`, `invite_tokens` | Emailed invitations, and their link hashes, apart |
| `outbox` | Everything waiting to be sent |
| `email_messages` | Volunteers' transactional mail, as sent |
| `external_publications`, `activity_pings`, `discord_role_grants` | What has been announced, who has been pinged, which Discord roles the bot granted |
| `crm_*` | The CRM and outreach mail |
| `federal_officials`, `sync_runs` | The officials directory and its refreshes |

### The outbox

A write that must cause a message queues an `outbox` row in the same
transaction. `OutboxWorker` claims rows with one atomic `UPDATE`, never a lock
held across a send, and `OutboxDispatcher` turns each into mail through
`EmailDispatcher`, the only thing that sends. Every message is recorded in
`email_messages` first, and no address gets more than ten an hour, however
many activities a script signs it up for. `ReminderWorker` queues the
day-before reminders every five minutes.

| `kind` | Payload | Sends |
|---|---|---|
| `signup.confirmed` | `signupId` | The confirmation, or the waitlist notice |
| `signup.promoted` | `signupId` | "You are in", off the waitlist |
| `activity.reminder` | `signupId` | The reminder, a day before |
| `member.welcome` | `volunteerId` | One welcome, the first time someone joins |

Until the first real deploy, `0001_schema.sql` is edited in place; nothing is
deployed, so there is nothing to migrate. **The first deploy freezes it**, and
every change after that is a new numbered script.

### Dapper and Npgsql fail at runtime, not compile time

| Trap | What happens | Fix |
|---|---|---|
| Mapping onto a value tuple | Dapper matches constructor parameters by name; a tuple's are `item1`, `item2` | Use a `record` |
| `timestamptz` → `DateTimeOffset` | Npgsql returns `DateTime`; Dapper wants an exact type match | Handler in `DapperTypeHandlers.cs` |
| `bigint` → `ulong` | Npgsql returns `long` | Handler **and** `SqlMapper.RemoveTypeMap(typeof(ulong))`: the built-in map preempts handlers for parameters |
| `text[]` → `string[]` | Npgsql reports the column as `System.Array` | Handler |
| `date` → `DateOnly` | Will not bind as a parameter | Handler |

A stub reader that reports `typeof(string[])` passes a unit test and fails
against real Postgres. Test against the real thing.

## 5. Configuration

| Key | Default | Means |
|---|---|---|
| `ManyHands:Enabled` | `false` | Everything. Off, nothing below is read |
| `ManyHands:Seed` | `false` | Run the `_seed_` scripts. Local development only |
| `ConnectionStrings:ManyHands` | — | Postgres. Required when enabled |
| `Email:Provider` | `log` | `log` writes every message to the log and sends nothing; `resend` sends |
| `Email:ApiKey` | — | Resend. Required when the provider is `resend` |
| `Email:FromAddress` | `volunteer@localhost` | On a mail subdomain with its own SPF, DKIM and DMARC |
| `Email:FromName` | `PhillyPause` | |
| `Email:ReplyToAddress` | — | Where replies to a volunteer's mail go |
| `Email:PublicBaseUrl` | `http://localhost:5203` | The origin every link in an email is built on |

The organisation's name and its timezone, `America/New_York`, are constants
in `ManyHands.Core/Organisation.cs`, not configuration: there is no second
organisation to configure for. Every time in an email is Philadelphia's
clock, labelled "Eastern time". Every route is in
`ManyHands.Core/ManyHandsPaths.cs`: volunteers' pages under `/volunteer`,
organizers' under `/team`.

Locally, secrets go in `dotnet user-secrets` on `Website.Portal`; in
production they are App Platform environment variables
(`ManyHands__Enabled`, `ConnectionStrings__ManyHands`).

## 6. Running it locally

The site runs without any of this. To run ManyHands too, start a Postgres and
point the portal at it:

```powershell
docker run -d --name manyhands-db -e POSTGRES_PASSWORD=dev -p 5432:5432 postgres:17-alpine
cd Website.Portal
dotnet user-secrets set "ManyHands:Enabled" "true"
dotnet user-secrets set "ConnectionStrings:ManyHands" "Host=localhost;Username=postgres;Password=dev;Database=postgres"
dotnet run
```

The schema is applied at startup. `ManyHands.Tests` starts its own container,
so `dotnet test` needs Docker running.

## 7. Status

Coming across in phases, one PR each.

| Phase | State |
|---|---|
| 1. Projects, schema, migrations, tests | **built** |
| 2. Volunteers, activities, signup and waitlist, sign-in tokens, outbox, mail, reminders | **built** |
| 3. Public pages: activities, signup, join, sign-in, my activities, unsubscribe, calendar | not started |
| 4. Organizer console: editor, roster, check-in, invites, roles, export | not started |
| 5. CRM, outreach email and the inbound webhook, the officials directory | not started |
| 6. Discord | not started |
| 7. The site links to it; Small Print, privacy and hosting updated | not started |
