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
| Joining | Name, email and interests; then one email asking them to confirm the address is theirs, with a link that removes them just as easily (Joining, below) |
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
- **Static server rendering, but for the organizers' app.** The pages are
  server-rendered Razor components and `EditForm`s, like the rest of the
  site. The exception is the organizers' app (`TeamApp`): the team pages on
  its list run interactive on the server, on Syncfusion's grid. Either way
  they call the data layer directly; there is no HTTP API and no WASM.
- **The site never needs ManyHands.** Off, nothing of it is registered and the
  site needs no database. On, the home page still never touches the
  database: its content is hand-edited and the account bar reads only the
  sign-in cookie, so an outage cannot take the front page down.
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
  production, and set outside Development it stops the site starting.
  `example.test` addresses and `555-01xx` numbers, never real people, and
  no activities.

## 3. Shape

| Project | Holds |
|---|---|
| `ManyHands.Core` | Records, enums and results. No framework, no packages |
| `ManyHands.Data` | Dapper repositories, the DbUp migrations, mail, Discord, the federal dataset, the background workers |
| `ManyHands.Tests` | xunit, against real Postgres in a container |
| `Website.Portal` | The pages, under its own components, and the few endpoints a page cannot be: the inbound mail webhook, one-click unsubscribe, the ICS feeds |

`Website/Website.Portal/Program.cs` calls `AddManyHands()` and `MigrateManyHands()`
only when the flag is on. The background workers are hosted services in the
same process: there is one instance, so there is nothing to coordinate. With
the flag off, `UseManyHandsGate` answers every `/volunteer`, `/events` and `/team` path
with the ordinary 404, so no page reaches for a service that was never
registered.

### The volunteer pages

Static SSR Razor components in `Website/Website.Portal/Components/Volunteer`, on
`VolunteerLayout` (the site's own header and footer, plus the area's nav and
`volunteer.css`). Helpers that are not components live in
`Website/Website.Portal/Volunteer`.

| Route | Page |
|---|---|
| `/events` | Not a page here: the front page's Events dialog (`EventsList`), what is coming up, filterable by type, with the public calendar feed. Its filters link back to `/events?type=...` |
| `/volunteer/activity/{id}` | One activity and its signup form |
| `/join` | Joining is not a page here: it is the front page's Join us dialog (On the site, below). Every Join link goes to it |
| `/volunteer/sign-in` | Ask for a sign-in link |
| `/volunteer/sign-in/{token}` | Opening the link shows a button; **pressing it** spends the token and lands on the front page, not `/volunteer/me`, until the volunteer pages are ready for readers |
| `/volunteer/me` | Signups and cancelling, interests and email, Discord unlink, the personal calendar feed, export, delete, sign out |
| `/volunteer/check-email` | The front page with the Check your email dialog, where joining redirects someone not yet confirmed: what the email looks like, and what to do if it does not come. `?again=true` when it was sent again |
| `/volunteer/confirm/{token}` | Not a page here: the front page with the Finish joining dialog, where the link in a joining email lands. It greets them by name; **pressing** "Yes, that's me" confirms the address and signs them in, and "That's not me" is a link to removal |
| `/volunteer/welcome` | The front page with the Welcome dialog, where confirming redirects: what happens now, the primer, back to the front page. It does not link to their page yet |
| `/volunteer/remove/{key}` | The front page with the Leave the list dialog: "Not you, or changed your mind?" Asks, then deletes them, as the button on their page does, and signs this device out if it was them. Never an organizer |
| `/volunteer/unsubscribe/{key}` | Asks, then unsubscribes |
| `/volunteer/link-discord/{token}` | Where `/link` in Discord sends someone: signed in, one press links the account |

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
  carries the volunteer id, plus a name and role for the account bar to show,
  so no page waits on the database to say who is signed in. Access never reads
  those: every team page reads the role fresh, so a removed organizer stops
  being one at once. The name and role in the cookie are re-checked against
  the database every five minutes, and a deleted volunteer is signed out.
- **A signed-in page is never cached** (`Cache-Control: no-store, private`):
  it carries the reader's name, and a copy would greet the next reader as them.
- **The keys that sign the cookie live in Postgres** (`data_protection_keys`).
  In the container they would be replaced by every deploy, signing everyone
  out on each merge to `main`.

### Joining

Anyone can type any address into the Join us form, so joining asks the
person to confirm the address is theirs before ManyHands counts them as a
member who hears about new activities. It asks gently. These are volunteers,
and doing nothing is a fine answer.

- **One email, then one reminder, then silence.** Joining queues
  `member.confirm` at once and `member.nudge` to wait three days
  (`VolunteerRepository.NudgeAfter`). The reminder says it is the last and
  sends nothing to anyone who has confirmed, removed themselves or
  unsubscribed by then. Joining again before confirming sends the email
  again, never a second reminder.
- **Every joining email carries a removal link** as plainly as the confirm
  link: `/volunteer/remove/{key}` deletes the person outright, with no
  sign-in, because someone else may have typed the address.
- **The confirm link lasts 30 days**, or until any one of them is pressed,
  which spends them all. A month-old email cannot sign anyone in. Pressing
  one signs the person in, since it proves they read mail there.
- **Unconfirmed is not unwelcome.** They are on `/team/members`, marked "not
  confirmed yet", and can sign up for activities as anyone can, with the
  mail that goes with that. What they do not get is interest pings: those
  wait for a confirmed address. Signing in confirms too, and so does
  accepting an invite, which was mailed to that address.
- **An activity signup from a new address offers it once**: the signup's
  own confirmation email carries the confirm link, with no reminder after.
- **Nobody is removed for not answering.** An unconfirmed person stays until
  they remove themselves.

`volunteers.email_verified_at` is the state: set, the address is confirmed.

### The team pages

Under `/team`, in `Website/Website.Portal/Components/Team`, all behind
`TeamPolicies.Organizer`. A member who wanders onto one gets the ordinary 404:
there is nothing there for them. The role is read from the database on every
request.

| Route | Page | Who |
|---|---|---|
| `/team` | Every activity, drafts included, with the numbers; the participation CSV | Organizers |
| `/team/activity/new`, `/team/activity/{id}/edit` | The editor. Times are typed in Philadelphia's clock and stored as instants | Organizers |
| `/team/activity/{id}` | Publish or unpublish; the roster, one big button per name for check-in; walk-ins | Organizers |
| `/team/activity/{id}/check-in` | The QR code for the door | Organizers |
| `/team/members` | Everyone, newest first, one line each, as a grid: a filter row that narrows the list as you type, sorting, a page at a time. Email, the day they joined (MM/dd/yyyy, Philadelphia's clock), whether they have confirmed, their role and what they like doing. Icon buttons send the joining email again, change a role and delete | Organizers see and resend; admins change and delete |
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
- **What the members page does, `MemberAdminRepository` checks again**,
  reading the role of whoever pressed from the database, whatever the page
  offered. Changing a role is admins' alone, and the last admin cannot stop
  being one. Deleting is admins' alone, is the same anonymising deletion as
  a person's own button, asks first, and never takes an admin or the person
  pressing. Sending the joining email again queues `member.confirm` as
  joining again would, never to someone confirmed or unsubscribed, and not
  twice in fifteen minutes.
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

### Discord

In `ManyHands/ManyHands.Data/Discord`, running in the portal process like the other
workers. **The token is the switch**: with `Discord:BotToken` set,
`DiscordGateway` holds one connection and is the sender; without it,
`LoggingDiscordSender` writes what it would have sent and reports it
delivered, so a token added later does not announce months-old work.

| Does | How |
|---|---|
| Announces a published activity | One post in the announcement channel, with a public thread opened on it |
| Keeps the thread alive | "Someone just signed up for this one", naming nobody: a thread is public |
| DMs the interested | Instead of the email, for people who linked Discord. Opted out of email is opted out of DMs: one consent, two pipes |
| DMs on signup | Beside the confirmation email, never instead of it |
| `/activities`, `/mysignups`, `/link` | Slash commands on our one server; every reply is visible only to the person who asked |
| Gives the volunteer role | A sweep every 15 minutes grants `Discord:RoleId` to linked volunteers and takes back only roles it gave itself |

**Linking.** `/link` replies with a sealed, fifteen-minute link naming the
caller's Discord account. Opening it signed in on the site, and pressing the
button, links the two. Discord proves one side and the sign-in the other, so
nobody can link an account that is not theirs. An account belongs to one
person: linking it to someone moves it. Unlinking is a button on
`/volunteer/me`, and deleting someone unlinks them; either way the role goes
at the next sweep.

Nothing Discord does can fail mail. A failed DM or thread note is logged and
the row still succeeds, because a retry would send the email again. The one
Discord step that does fail its row is the announcement itself, whose claim
stays open for the retry.

The bot asks for the Guilds intent only, so the Discord application needs no
privileged intents. In the server it needs Send Messages, Create Public
Threads and Send Messages in Threads, and Manage Roles with its own role
above `Discord:RoleId`.

### On the site

Where the website meets ManyHands, each with the fallback it shows when
ManyHands is off or has nothing to say. Off, no page links to it or mentions
it; `ManyHandsGateTests` holds that, and `SiteWiringTests` the flag-on side.

| Where | On | Off |
|---|---|---|
| The account bar, top of every page | Signed out: Events, Join, **Sign in**. Signed in: "Signed in as *name*", My page, Events, **Team** for organizers, Sign out. Events and Join load the page in full (`data-enhance-nav="false"`): enhanced navigation would leave the dialog shut | Not rendered |
| Join us dialog, front page | The signup form itself (`JoinForm`): name, email, interests. Pressing Join first checks the connection (`join.js`), and without one keeps the form and says so. It posts to `/join`; someone new is redirected to Check your email, a member already confirmed is answered in the dialog; if the database is unreachable it says so | No form and no email signup: the dialog says what joining means |
| Events dialog, front page | What is coming up; if the database is unreachable it says so | Not rendered, and `/events` is a 404 |
| I can help | Opens Events | Opens Join us |
| Small Print, cookies | Adds the sign-in cookie | One cookie |
| `/privacy` | Adds what ManyHands holds, from `Content/_manyhands/privacy.md` | The base page |

**The front page's Next event box is not ManyHands'.** It is hand-edited in
`Home.razor` and shows what PhillyPause chooses to lead with, flag on or off.
A published activity never replaces it; `SiteWiringTests` holds that.

A file in `Content/_manyhands/` supplements the page with the same slug,
at its `<!-- manyhands -->` marker or else at the end, only while ManyHands is
on. A supplement for a page that does not exist stops the site starting.

## 4. Data

Postgres, one schema (`public`), defined by
[`ManyHands/ManyHands.Data/Scripts/0001_schema.sql`](../../ManyHands/ManyHands.Data/Scripts/0001_schema.sql).

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
| `signup.confirmed` | `signupId` | The confirmation, or the waitlist notice; a DM beside it if they linked Discord, and an unnamed note in the activity's thread |
| `signup.promoted` | `signupId` | "You are in", off the waitlist |
| `activity.reminder` | `signupId` | The reminder, a day before |
| `member.confirm` | `volunteerId` | Joining from the site: mints a 30-day confirm link and emails it beside the removal link. Nothing if already confirmed |
| `member.nudge` | `volunteerId` | Queued to wait three days; the one reminder, if they are still unconfirmed and reachable |
| `member.welcome` | `volunteerId` | One welcome, the first time someone joins by an accepted invite |
| `activity.published` | `activityId` | The Discord post and its thread, once (claimed in `external_publications`); then a DM to everyone interested who linked Discord, an email to everyone else, once each (claimed in `activity_pings`) |
| `invite.sent` | `inviteId` | Mints the invite link and emails it |

`0001_schema.sql` has run in production, so it is frozen: every change is a
new numbered script, and an applied script is never edited.

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
| `Discord:BotToken` | — | The bot. Unset, every Discord call is a log line |
| `Discord:Enabled` | `false` | Says this environment must have a bot: set with no token, startup fails |
| `Discord:GuildId` | — | PhillyPause's server. Required with a token |
| `Discord:AnnouncementChannelId` | — | Where announcements go. Unset, the server's system channel, else the first the bot may post in |
| `Discord:RoleId` | — | The role every linked volunteer holds. Unset, nobody is given one, and any the bot gave are taken back |
| `Discord:SendDelay` | 250 ms | The pause between DMs, and between role edits |
| `SYNCFUSION_LICENSE_KEY` | — | The Syncfusion Community Licence for the organizers' app, as an environment variable of that name. Unset, its pages work and show a trial banner. Locally the AppHost passes on the machine's own |
| `Discord:RoleSyncInterval` | 15 minutes | How often roles are reconciled |

The organisation's name and its timezone, `America/New_York`, are constants
in `ManyHands/ManyHands.Core/Organisation.cs`, not configuration: there is no second
organisation to configure for. Every time in an email is Philadelphia's
clock, labelled "Eastern time". Every route is in
`ManyHands/ManyHands.Core/ManyHandsPaths.cs`: volunteers' pages under `/volunteer`,
organizers' under `/team`, and `/join` and `/events` are front-page dialogs.

Locally, secrets go in `dotnet user-secrets` on `Platform/Platform.AppHost`,
which passes its `ManyHands`, `Email`, `Discord` and `FederalOfficials`
sections on to the site (`Website.Portal` has no secrets store of its own):

```powershell
dotnet user-secrets set --project Platform/Platform.AppHost "Email:Provider" "resend"
dotnet user-secrets set --project Platform/Platform.AppHost "Email:ApiKey" "re_..."
```

In production they are App Platform environment variables
(`ManyHands__Enabled`, `ConnectionStrings__ManyHands`, `Discord__BotToken`).

## 6. Running it locally

With Docker Desktop running:

```powershell
dotnet run --project Platform/Platform.AppHost
```

The Aspire AppHost starts a Postgres 17 container and the site with
ManyHands on and seeded, and opens the dashboard at
<https://localhost:17203>: every resource, its console, and the site's
structured logs, traces (each request, with its database queries timed
inside it) and metrics. The site is at <http://localhost:5203>.

The database is **ephemeral**: every run starts from nothing, applies the
schema and seeds the fictional data in `9999_seed_dev.sql` again —
`organizer@example.test` (an admin), `ben@example.test`, `cai@example.test`,
and a little CRM. Locally, nothing survives a restart; production is
another matter (section 7). It seeds **no activities**: an invented event reads
as a real one, so the Events list starts empty, and every event on it is one
an organizer made at `/team/activity/new`.

Mail is only logged locally. To sign in, ask for a link on
`/volunteer/sign-in`: in development, with no mail provider, the page shows
the link it logged. Emails are in the dashboard's logs for `website`,
among them the joining email with its confirm and removal links: join on
<http://localhost:5203/join>, then find "One click to finish joining" there.
Signed in as `organizer@example.test`, `/team/members` shows the newcomer.

`dotnet run --project Website/Website.Portal` still runs the site alone, with
ManyHands off and no database. `ManyHands.Tests` starts its own container,
so `dotnet test` needs Docker running too.

## 7. Status

All of it is built and tested, flag on and flag off. It is **on in
production** at phillypause.org, with its managed Postgres
([`HOSTING.md`](../Platform/HOSTING.md) under "ManyHands"). Migration
`0001` has run there, so it is now as fixed as any other: changes go in a
new script. For now, nothing sends a volunteer to their own page: signing
in and confirming land on the front page.
