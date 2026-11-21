# Act

The action centre: it helps a member or a visitor find who represents them,
from Congress to City Hall, and call or write to them about AI in fifteen
minutes. It replaces the front page's link to ControlAI's tool with our own,
for Greater Philadelphia: Philadelphia, Bucks, Chester, Delaware and
Montgomery counties. Anyone outside them is sent to ControlAI's
(`https://act.controlai.org/`), which covers everywhere.

It needs no database. With ManyHands on, it also keeps a log of who
contacted whom, which is ManyHands' (below, and
[`MANYHANDS.md`](../ManyHands/MANYHANDS.md)).

## A dialog on the front page

The action centre is the front page's **Act** dialog, opened like Join by
a hidden radio (`#act-open`) and CSS, with no script. The ways in are kept
prominent:

- **ACT**, the last item in the front page's nav band, after JOIN, at its
  right edge; full width under the other links on a phone.
- A band of ink under the lead story's headline, "Call or write the people
  who represent you", the front page's one call to action.
- Organize's "Who to call".
- **Act** in the header of every inner page, and in a member's nav,
  as links to `/act`.

On the plain front page the dialog holds its first view (`ActStart`). Every
address below is the front page with the dialog open on a view of its own:
each page passes its markup to `Home` as `ActView` and renders its own
`PageMeta`, as `NotFound.razor` does for Not found. Links between views load
each address in full (`data-enhance-nav="false"` on the dialog and on every
link into it): enhanced navigation would patch the radio's `checked`
attribute, which a radio the reader has clicked ignores, and the dialog would
shut. Closing the dialog leaves the reader on the front page, as /join does.

The dialog's three places, "Your officials", "Greater Philly" and "How it
works", are tabs under its title bar (`ActTabs`). It is styled by `act.css`,
which re-points the tokens.css names it uses at the broadsheet's ink, ivory
and red inside `.d-act`, and lays out by the dialog's width (container
queries) rather than the window's.

**Never a list of dozens.** Nobody reads one, so the officials are reached
by filter chips instead (`FilterChips`): each is a link to the view with
that choice made and says how many it would show, the chosen one is ink, and
one that would show nobody is not a link. "Your officials" shows one
place at a time, Washington first; the directory lists nobody until
something is chosen, and never more than a dozen (`DirectoryFilter.Shown`),
past which it says how many matched and asks for one more choice. Every
state is an address, so the chips are plain links and the search a GET form.

## Views

All static server rendering. Code in `Website/Website.Portal/Components/Act`
and `Website/Website.Portal/Act`; the addresses are in `ActPaths`.

| Route | View |
|---|---|
| `/act` | Find your officials (an address, or districts picked by hand), the three ways in, the two motion tutorials, the ask, the county map, the explainers. The address form posts here from wherever it was shown, so a problem with the address comes back with the dialog open. An address outside the five counties, in Pennsylvania or not, comes back with a button to ControlAI's action page instead of a list, and a line under the finder points anyone outside them there before they type |
| `/act/yours?cd=&ss=&sh=&county=` | The officials for those districts, one place at a time: Washington, then `in=harrisburg` or `in=local`, each a chip with its count. A Philadelphian's City Hall is the citywide officials plus the member for the council district they choose (`cc=`). District numbers in the address bar, never the address |
| `/act/officials?body=&county=&q=` | The directory behind a filter (`DirectoryFilter`): a government (Congress, Statewide, State Senate, State House, City Hall, County), a county, and a name or a district number. A bare number is a district, so `182` is House district 182; names match without accents or punctuation. The county map's links land here with `?county=` |
| `/act/officials/{key}` | One official: contacts, bio, AI record with sources, committees |
| `/act/call/{key}` | The numbers, a script that works as it stands, one with the reader's own name and reason, and "I called" |
| `/act/write/{key}` | A draft to change and send from the reader's own email or the office's form, and "I sent it" |
| `/act/write?to=&to=` | One message to several, a copy each (`WriteManyPage`): reached from "Write to all of them" on each place in Your officials. Each recipient is a chip whose cross drops them; one left is the ordinary write page |
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

**No caveats.** Nothing a reader sees says whether an entry or an item has
been checked: the two `verified:` flags are the editors' checklist, and the
directory is checked before it goes live. `Act:ShowUnverified` is true in
`appsettings.Development.json` and false everywhere else, so locally every AI
record item is shown and in production an unchecked one is left out,
unmarked either way. Contact details are shown regardless, so check them
first.

**Matching.** The senators and statewide officers represent every
Pennsylvania address; a House or General Assembly member matches on the
district number; the city and counties match on the county. The Census
geocoder does not know Philadelphia's council districts, so a Philadelphian
chooses theirs from chips, with a link to City Council's site for anyone
unsure; until they do, City Hall shows the citywide officials alone.

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

## Writing to several at once

Your officials offers "Write to all of them" for each place with more than
one to write to, leaving out anyone departing, up to `MailMerge.Most`. The
reader answers About you once and gets one draft (one model call), which
greets and asks with tokens: `{salutation}` ("Senator Fetterman",
"Commissioner Reuther") and `{ask}` (what `TheAsk` asks of that office),
with `{name}` for the full name. The form of address and the ask both
change with the office, so a copy is filled for each. A model's reply that
dropped either token is refused for the template.

The page is its own walkthrough: three numbered steps, the ones ahead named
before they are reached, and the last a row per official. A row is "Email
Senator X", or, for an office with only a form, "Copy theirs" and "Open
their form"; each copy is filled from the edited text at the moment of the
click (`act.js`, which must fill exactly as `MailMerge.Fill` does). Each row
is ticked off as it is opened, and a line under the list says how many are
done and who is next. Without script, each link carries the draft as
written, filled for its official. Nothing is sent from here and nothing
typed is kept; with ManyHands on, "I sent them" logs one action per official
ticked (`LogMany`). There is no "copy an earlier message": that would mean
keeping what people wrote.

## The ask

What each level is asked to do is in `Website/Website.Portal/Act/TheAsk.cs`,
which every draft and script uses, and in words on
`Content/act/learn/the-ask.md`. Change both together.

## Motion

`HowToCall` and `HowToWrite`: four steps each, lit in turn on a twelve-second
loop, each icon doing one small thing; on the page that writes to several,
`HowToWrite`'s last step fans one letter into three. Hand-drawn SVG and CSS keyframes in
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
| `Act:ShowUnverified` | `false` (true in Development) | Show AI record items nobody has checked. Never marked |
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
