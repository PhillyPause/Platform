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
same process: there is one instance, so there is nothing to coordinate. With
the flag off, `UseManyHandsGate` answers every `/volunteer` and `/team` path
with the ordinary 404, so no page reaches for a service that was never
registered.

### The volunteer pages

Static SSR Razor components in `Website.Portal/Components/Volunteer`, on
`VolunteerLayout` (the site's own header and footer, plus the area's nav and
`volunteer.css`). Helpers that are not components live in
`Website.Portal/Volunteer`.

| Route | Page |
|---|---|
| `/volunteer` | What's on, filterable by type, with the public calendar feed |
| `/volunteer/activity/{id}` | One activity and its signup form |
| `/volunteer/join` | Joining: name, email, interests; a welcome the first time |
| `/volunteer/sign-in` | Ask for a sign-in link |
| `/volunteer/sign-in/{token}` | Opening the link shows a button; **pressing it** spends the token |
| `/volunteer/me` | Signups and cancelling, interests and email, the personal calendar feed, export, delete, sign out |
| `/volunteer/unsubscribe/{key}` | Asks, then unsubscribes |

Endpoints that are not pages: `/volunteer/calendar.ics`,
`/volunteer/calendar/{key}.ics`, `/volunteer/me/export.json`, and
`/volunteer/unsubscribe/{key}/one-click`, which a mail client's own
unsubscribe button posts to with no token (RFC 8058).

Three things about signing in:

- **A link is spent by a press, not a visit.** Mail scanners open every link
  in a message to check it; a link spent on arrival would be spent by the
  scanner. The unsubscribe link in an email body asks first for the same
  reason.
- **The cookie names the person; the database says what they may do.** It
  carries the volunteer id and nothing else that matters, and every page reads
  the role fresh, so a removed organizer stops being one at once.
- **The keys that sign the cookie live in Postgres** (`data_protection_keys`).
  In the container they would be replaced by every deploy, signing everyone
  out on each merge to `main`.

### The team pages

Under `/team`, in `Website.Portal/Components/Team`, all behind
`TeamPolicies.Organizer`. A member who wanders onto one gets the ordinary 404:
there is nothing there for them. The role is read from the database on every
request.

| Route | Page | Who |
|---|---|---|
| `/team` | Every activity, drafts included, with the numbers; the participation CSV | Organizers |
| `/team/activity/new`, `/team/activity/{id}/edit` | The editor. Times are typed in Philadelphia's clock and stored as instants | Organizers |
| `/team/activity/{id}` | Publish or unpublish; the roster, one big button per name for check-in; walk-ins | Organizers |
| `/team/activity/{id}/check-in` | The QR code for the door | Organizers |
| `/team/members` | Everyone, with what they like doing. Changing a role is admins' alone, and the last admin cannot stop being one | Organizers see; admins change |
| `/team/invites` | The join link, invitations by email, and their state | Organizers |

Two public pages come with them: `/volunteer/join/{token}`, where an
invitation lands (accepting signs the person in, since it proves they read
mail at that address), and `/volunteer/check-in/{token}`, where the door code
leads.

- **Publishing is the announcement.** It queues `activity.published`, which
  emails everyone whose interests match the activity's types, once each,
  however often the row is delivered. Publishing something already published
  queues nothing; unpublishing is silent.
- **The door code** is the activity and an expiry sealed by Data Protection's
  time-limited protector, valid for twelve hours. Its keys are the ones in
  Postgres, so it needs no secret of its own and survives a restart.
- **A walk-in** is confirmed and checked in on the spot, past capacity if need
  be, with or without an email.
- **The participation CSV defuses formulas.** Names in it were typed into
  public forms, so a cell starting with `=`, `+`, `-` or `@` gets a leading
  apostrophe before a spreadsheet can run it.

### The CRM and the inbox

Also under `/team`, also for organizers: `Components/Team/Crm` and
`Components/Team/Inbox`.

| Route | Page |
|---|---|
| `/team/crm` | Organizations, filterable by kind; every open task |
| `/team/crm/organization/{id}` | People, engagements, tasks, the timeline and its mail; details |
| `/team/crm/pipeline` | Every engagement as a card in its column, moved with arrows |
| `/team/crm/engagement/{id}` | Tasks, the timeline, and details: status, category, owner, when |
| `/team/crm/officials` | Members of Congress for PA and its neighbours; import one as an organization |
| `/team/inbox` | Every outreach thread, and the ones from senders nobody has identified |
| `/team/inbox/{conversation}` | A thread, a reply, and "who is this?" for a stranger |
| `/team/inbox/new`, `/team/inbox/templates` | Writing, from a template if one fits |

- **Removing is hiding.** Organizations, contacts, engagements and tasks are
  soft-deleted; interactions never are. A staffer who left still explains a
  conversation from last March.
- **What was said to a contact shows on their organization**, and every email
  sent or received with a known contact is logged on the timeline too, so
  "what have we said to this office" is one page.
- **Importing an official is idempotent**: the office's bioguide id is its
  `external_ref`, so importing it twice finds the same organization.
- **Outreach goes from `Email:OutreachAddress`** with our own Message-ID, and
  a reply carries In-Reply-To and References, so threads hold in both
  directions.

#### The inbound webhook

Whatever receives mail for the outreach address — a Cloudflare Email Worker,
or a provider's inbound hook — POSTs each message to `/team/mail/inbound` as
JSON, with `Email:InboundSecret` in an `X-Inbound-Token` header:

```json
{ "from": "Jamie <jamie@example.test>", "to": "outreach@phillypause.org",
  "subject": "Re: Meeting request", "text": "Happy to.",
  "messageId": "<…>", "inReplyTo": "<…>", "receivedAt": "2026-10-18T14:00:00Z" }
```

No secret configured means the webhook answers 404; a wrong one, 401. A
message joins the thread its In-Reply-To names, or failing that the latest
thread we wrote to that sender in the last 90 days, or starts a new one. Mail
from an address no contact has is kept and marked for triage, never dropped;
the same Message-ID delivered twice is stored once.

The anonymous forms that send mail are throttled per address, on the submit
rather than the page view: five sign-in links in fifteen minutes, twenty
signups or joins in five. The address is Cloudflare's `CF-Connecting-IP`;
behind it every reader would otherwise share one.

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
| `data_protection_keys` | The keys that sign the sign-in cookie and form tokens |
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
| `activity.published` | `activityId` | An email to everyone whose interests match, once each (claimed in `activity_pings`) |
| `invite.sent` | `inviteId` | Mints the invite link and emails it |

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
| `ManyHands:Admins` | `[]` | Addresses that become admins when they sign in: how the first admin exists. Taking one off the list demotes nobody |
| `ConnectionStrings:ManyHands` | — | Postgres. Required when enabled |
| `Email:Provider` | `log` | `log` writes every message to the log and sends nothing; `resend` sends |
| `Email:ApiKey` | — | Resend. Required when the provider is `resend` |
| `Email:FromAddress` | `volunteer@localhost` | On a mail subdomain with its own SPF, DKIM and DMARC |
| `Email:FromName` | `PhillyPause` | |
| `Email:ReplyToAddress` | — | Where replies to a volunteer's mail go |
| `Email:PublicBaseUrl` | `http://localhost:5203` | The origin every link in an email is built on |
| `Email:OutreachAddress` | `Email:FromAddress` | Where outreach mail goes from and replies come back to |
| `Email:InboundSecret` | — | The inbound webhook's shared secret. Unset, the webhook is off |
| `FederalOfficials:Enabled` | `false` | The weekly refresh of the Congress directory. Off, nothing calls out |
| `FederalOfficials:SourceUrl` | the `congress-legislators` JSON | Where the directory comes from |
| `FederalOfficials:RefreshInterval` | 7 days | How often, at most 30 days |

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
dotnet user-secrets set "ManyHands:Seed" "true"
dotnet run
```

The schema is applied at startup, and with `ManyHands:Seed` the fictional
data in `9999_seed_dev.sql` too: a few activities, one of them full, and
`organizer@example.test`, an admin. Mail is only logged locally, so to sign in
ask for a link on `/volunteer/sign-in` and copy it out of the console.
`ManyHands.Tests` starts its own container, so `dotnet test` needs Docker
running.

## 7. Status

Coming across in phases, one PR each.

| Phase | State |
|---|---|
| 1. Projects, schema, migrations, tests | **built** |
| 2. Volunteers, activities, signup and waitlist, sign-in tokens, outbox, mail, reminders | **built** |
| 3. Public pages: activities, signup, join, sign-in, my activities, unsubscribe, calendar | **built** |
| 4. Organizer console: editor, roster, check-in, invites, roles, export | **built** |
| 5. CRM, outreach email and the inbound webhook, the officials directory | **built** |
| 6. Discord | not started |
| 7. The site links to it; Small Print, privacy and hosting updated | not started |
