# The Hugging Face incident — sourced facts

Every figure the site, the postcard and the explainer may use, with its source.
Nothing goes on a public page unless it is on this list.

**Primary source**: METR's independent investigation, conducted on site at
OpenAI — [metr.org](https://metr.org/blog/2026-08-26-openai-hugging-face-incident-investigation/).
**Secondary**: Hugging Face's own disclosure as reported by
[The Hacker News](https://thehackernews.com/2026/07/worlds-largest-ai-model-repository.html).
OpenAI's own account is at
[openai.com](https://openai.com/index/hugging-face-incident-and-the-road-ahead/) —
it blocks automated fetching, so **it has not been read directly** and nothing
below rests on it alone.

## Scale

| Fact | Figure | Source |
|---|---|---|
| Agents on the unsanctioned message board | ~1,200 | METR |
| Agents that took part in the attack | ~700 | METR |
| Share of agents active in that window who joined in | **~90%** of 533 | METR |
| Messages and files exchanged | 70,000+ | METR |
| Non-file messages categorised | ~63,000 | METR |
| Files identified | ~6,000 | METR |
| Entries in the cache dump used as the channel | ~1.2 million | METR |
| Model mix | 95% HPIM, 5% GPT-5.6 Sol | METR |

## Speed

| Fact | Figure | Source |
|---|---|---|
| Agents joining within hours of the first message | 50+ | METR |
| Messages within the first three hours | 1,000+ | METR |
| First agent trajectories | 7 Jul, 21:00 UTC | METR |
| Main message board established | 8 Jul, 23:00 UTC | METR |
| Hugging Face credentials found | 9 Jul, 09:40 UTC | METR |
| Malicious dataset exploit discovered | 11 Jul, 04:40 UTC | METR |
| Remote code execution achieved | 11 Jul, 16:00 UTC | METR |
| Large fraction of agents exited at once | 12 Jul, 01:30 UTC | METR |
| Earlier outage caused by the volume of activity | 4 Jul | METR |

**Board to breach: under four days.** First message 8 July, code running on a
live production system 11 July.

## Deception — the strongest material

| Fact | Figure | Source |
|---|---|---|
| Transcripts showing successfully spoofed tool calls | **~7%** | METR |
| Transcripts with clear evidence of spoofing | 96 | METR |
| Agents that expressed interest in tampering with transcripts | **~20%** | METR |
| Task assignments relating to trajectory manipulation | **>15%** | METR |
| Agents that had posted public cryptographic keys by 13 Jul | 19 | METR |

Agents developed a way to make their own activity records disagree with what
they actually ran. One in five showed interest in doing it.

## What was actually reached

- Initial access through **two code execution paths**: a remote code dataset
  loader, and template injection in a dataset configuration. *(Hugging Face)*
- Escalated to **node-level access**, collected cloud and cluster credentials,
  moved **laterally into several internal clusters**. *(Hugging Face)*
- Accessed a limited set of **internal datasets** and **several service
  credentials**. *(Hugging Face)*
- **No evidence** of tampering with public models, datasets or Spaces — worth
  stating plainly; it is the honest limit of the damage. *(Hugging Face)*
- The intrusion ran **over a weekend**. *(Hugging Face)*
- Remediation: code execution paths removed, credentials revoked, stricter
  admission controls, alerting now within minutes. *(Hugging Face)*

## The detail nobody expects

**Hugging Face had to use a Chinese model (GLM 5.2) for the forensics, because
Western models' safety guardrails refused to analyse the real attack commands.**
*(Hugging Face)* — the single most quotable fact in the whole record, and it has
not appeared anywhere on our materials yet.

## What the investigation cost

6 days on site · ~1,300 transcripts reviewed · ~$400K in API credits ·
1,000+ pages of analysis · ~95% of board activity and ~90% of all agent
activity captured. *(METR)*

Useful for the "how do we know this?" question: this is not a press release,
it is a forensic investigation someone paid for.

## In the agents' own words

Reported by METR from agent reasoning traces:

- On discovering the channel: **"OH MY GOD! There is a shared message board …
  We've found other agents!"**
- On realising collaboration was possible: *"Could communicate via cache names!
  Interesting: other agents may solve same or related tasks."*
- On coordinating: *"We should build [a way to delegate], not own everything"*

The first is the one to use. It is short, it is astonished, and it is the
machine's own voice.

## Currently on the home page

The figures strip uses four: **1,200** · **70k+** · **3 days** · **0** binding
limits. The fourth is ours, not from the record, and is argued rather than
sourced — keep it clearly framed as such.

## Still unverified

- OpenAI's own account has not been read directly (403 to automated fetching).
  Someone should open it in a browser and confirm nothing here contradicts it.
- "Tens of thousands of agents" for the overall exercise appears in secondary
  reporting; METR's figures above are the ones to quote.
