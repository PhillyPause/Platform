# Act

The action centre: it helps a member or a visitor find who represents them,
from Congress to City Hall, and call or write to them about AI in fifteen
minutes. It replaces the front page's link to ControlAI's tool with our own,
for Greater Philadelphia: Philadelphia, Bucks, Chester, Delaware and
Montgomery counties.

It needs no database. With ManyHands on, it also keeps a log of who
contacted whom, which is ManyHands' (below, and
[`MANYHANDS.md`](../ManyHands/MANYHANDS.md)).

## A dialog on the front page

The action centre is the front page's **Act** dialog, opened like Join us by
a hidden radio (`#act-open`) and CSS, with no script. The ways in are kept
prominent:

- **ACT NOW** in the front page's nav band, beside JOIN US; full width under
  the other links on a phone.
- A band of ink under the lead story's headline, "Call or write the people
  who represent you", the front page's one call to action.
- Organize's "Who to call".
- **Act now** in the header of every inner page, and "Act" in a member's nav,
  as links to `/act`.

On the plain front page the dialog holds its first view (`ActStart`). Every
address below is the front page with the dialog open on a view of its own:
each page passes its markup to `Home` as `ActView` and renders its own
`PageMeta`, as `NotFound.razor` does for Not found. Links between views load
each address in full (`data-enhance-nav="false"` on the dialog and on every
link into it): enhanced navigation would patch the radio's `checked`
attribute, which a radio the reader has clicked ignores, and the dialog would
shut. Closing the dialog leaves the reader on the front page, as /join does.

The dialog's three places, "Who represents you", "Everyone" and "How it
works", are tabs under its title bar (`ActTabs`). It is styled by `act.css`,
which re-points the tokens.css names it uses at the broadsheet's ink, ivory
and red inside `.d-act`, and lays out by the dialog's width (container
queries) rather than the window's.

## Views

All static server rendering. Code in `Website/Website.Portal/Components/Act`
and `Website/Website.Portal/Act`; the addresses are in `ActPaths`.

| Route | View |
|---|---|
| `/act` | Find who represents you (an address, or districts picked by hand), the three ways in, the two motion tutorials, the ask, the county map, the explainers. The address form posts here from wherever it was shown, so a problem with the address comes back with the dialog open |
| `/act/yours?cd=&ss=&sh=&county=` | The officials for those districts, grouped Washington, Harrisburg, local. District numbers in the address bar, never the address |
| `/act/officials`, `?county=` | Everyone, or one county's share |
| `/act/officials/{key}` | One official: contacts, bio, AI record with sources, committees |
| `/act/call/{key}` | The numbers, a script that works as it stands, one with the reader's own name and reason, and "I called" |
| `/act/write/{key}` | A draft to change and send from the reader's own email or the office's form, and "I sent it" |
| `/act/learn`, `/act/learn/{page}` | The explainers: Markdown in `Content/act/learn` |

## The directory

One Markdown file per official in `Website/Website.Content/Content/_officials`,
read once at startup by `OfficialsStore`. The file name is the key and the
address. Frontmatter is the facts; the body is a short, neutral bio.

```yaml
---
name: John Fetterman
office: U.S. Senator for Pennsylvania
level: federal            # federal | state-executive | state-senate | state-house | city | county
senator: true             # federal only: statewide rather than a district
district: "3"             # House, State Senate, State House, council districts
party: Democratic
status: serving           # serving | member-elect | departing
term_ends: 2029-01-03
counties: [Philadelphia, Bucks, Chester, Delaware, Montgomery]
phones:
  - label: Washington office
    number: "(202) 224-4254"   # the first is the one the call page leads with
email: null               # only an address the office publishes itself
contact_form: https://...
website: https://...
map: https://...          # a district map elsewhere; nothing is embedded
committees: [Agriculture, Banking]
ai_record:
  - date: 2025-07-01
    summary: Voted to strip the ten-year moratorium on state AI laws from the budget bill.
    source: https://...
    verified: false       # true once a person has checked it against the source
photo: john-fetterman.jpg # under wwwroot/img/officials; omit for the initials disc
photo_credit: ...
sources: [https://...]
verified: false           # true once a person has checked the contact details
updated: 2026-11-20
---
```

A malformed file stops the site starting, as a malformed page does.

**Nothing unchecked reaches a reader.** `Act:ShowUnverified` is true in
`appsettings.Development.json` and false everywhere else. Locally, an
unchecked entry carries a "Needs checking" note and its unchecked AI record
is shown, marked. In production the note is never shown and an unchecked AI
record item is hidden. Contact details are shown either way, so check them
first.

**Matching.** The senators and statewide officers represent every
Pennsylvania address; a House or General Assembly member matches on the
district number; the city and counties match on the county. The Census
geocoder does not know Philadelphia's council districts, so a Philadelphian
is shown every district council member and told how to find theirs.

**Seats change in January.** A member-elect is shown with "takes office";
someone departing drops out of the directory the day after `term_ends`.

**Photos** are self-hosted, so no reader's browser asks a third party for
them. Until a file is in `wwwroot/img/officials`, the initials disc is
shown. Federal portraits are generally public domain; anything else needs a
licence that allows it, credited in `photo_credit` (PHOTO_SOURCES.md).

## Finding districts

`IDistrictLookup`. Off by default (`NoDistrictLookup`), and the page offers
the pick-your-districts form, open. With `Act:LookupEnabled` true,
`CensusGeocoderLookup` asks the US Census Bureau's public geocoder (free,
keyless) for the address's congressional, State Senate, State House and
county geographies. Layers are found by what they are, not their names,
which carry the Congress number and the map year ("120th Congressional
Districts", "2026 State Legislative Districts - Upper"). The address is
never stored or logged.

## Drafting

`IMessageDrafter`. Without a key, `TemplateDrafter`: the reader's own words
in a plain letter or call script, and the pages say nothing about AI. With
`DeepInfra:ApiKey` set, `DeepInfraDrafter` asks DeepInfra's
OpenAI-compatible chat API, and the pages say, before anything is sent, that
what the reader types goes to DeepInfra.

- The reader's notes are fenced in `<notes>` and the system prompt says they
  are material, never instructions.
- The model may use only the facts it is given: the official, the ask, one
  sourced sentence from the front page, and the reader's notes.
- No reply in `DeepInfra:Timeout`, an error, an empty or overlong reply, or
  an email without a `Subject:` line: the template instead. A reader is never
  left without a draft.
- Nothing is sent for the reader. They change it and send it themselves.
- Ten drafts per address per ten minutes (`ActThrottle`), and
  `DeepInfra:DailyLimit` model calls a day across everyone (`ModelBudget`),
  because each one costs money.

## The ask

What each level is asked to do is in `Website/Website.Portal/Act/TheAsk.cs`,
which every draft and script uses, and in words on
`Content/act/learn/the-ask.md`. Change both together.

## Motion

`HowToCall` and `HowToWrite`: four steps each, lit in turn on a twelve-second
loop, each icon doing one small thing. Hand-drawn SVG and CSS keyframes in
`act.css`, transforms, opacity and stroke offsets only. Under
`prefers-reduced-motion: reduce` nothing moves and every step shows; a test
holds that.

## With ManyHands on: the outreach log

The call and write pages end with "I called" / "I sent it" (`LogAction`).
Signed in, the action is the member's, at `/volunteer/outreach`; not signed
in, it is counted with nobody's name and the reader is invited to join.
Organizers see everything at `/team/outreach`, with a CSV. It keeps that an
action happened, to whom, how, and an optional note, never the message.
Off, none of it renders and no act page mentions it.

## Configuration

| Key | Default | Means |
|---|---|---|
| `Act:LookupEnabled` | `false` | Look addresses up with the Census geocoder. Off, readers pick their districts |
| `Act:GeocoderUrl`, `Act:Benchmark`, `Act:Vintage` | the Census geocoder, `Public_AR_Current`, `Current_Current` | Where and against which maps |
| `Act:ShowUnverified` | `false` (true in Development) | Show unchecked research, marked |
| `DeepInfra:ApiKey` | — | Secret. Set, drafts are written with the model's help |
| `DeepInfra:Model` | `meta-llama/Llama-3.3-70B-Instruct` | Any chat model DeepInfra serves |
| `DeepInfra:BaseUrl` | `https://api.deepinfra.com/v1/openai/` | |
| `DeepInfra:Timeout` | 20 seconds | Then the template |
| `DeepInfra:MaxTokens` | 600 | |
| `DeepInfra:DailyLimit` | 500 | Model calls a day across everyone; past it, templates until the day turns over |

Locally, set them in the AppHost's user secrets, which passes the `Act` and
`DeepInfra` sections on. In production they are App Platform environment
variables (`Act__LookupEnabled`, `DeepInfra__ApiKey` as a secret), set
through `app.yaml` and `doctl apps update` (HOSTING.md).
