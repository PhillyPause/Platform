# Hosting

`phillypause.org` is live. It runs on DigitalOcean App Platform, behind
Cloudflare.

| | |
|---|---|
| App | `phillypause-platform`, id `a21db074-3a86-449d-810f-edb0d48e45d6` |
| Region | `nyc` |
| Size | `apps-s-1vcpu-0.5gb` — 1 shared vCPU, 512 MiB, $5/month |
| Default ingress | `phillypause-platform-ntkmt.ondigitalocean.app` |
| Spec | [`Deploy/digitalocean/app.yaml`](../Deploy/digitalocean/app.yaml) |

## The spec is the record

`Deploy/digitalocean/app.yaml` is what is deployed. Change it there and apply:

```bash
doctl apps update a21db074-3a86-449d-810f-edb0d48e45d6 \
  --spec Deploy/digitalocean/app.yaml
```

**Never change the app in the control panel.** The panel and the file then
disagree, the panel wins silently, and the next person reads the file and is
wrong. This has already cost one failed deploy: a bad `SOLUTION_FILE` was
corrected in the file but not applied, and the app kept building with the old
value.

`doctl apps propose --spec …` validates a spec, and its GitHub access, without
changing anything.

## Shipping

Merge to `main`. That is the whole process.

`main` is protected and requires `build-and-test`, so nothing reaches
production that has not already built and passed on Linux in Release with
warnings as errors. The spec sets `deploy_on_push: true`, so the merge triggers
a build on App Platform. About three minutes end to end.

```bash
# watch the current deployment
doctl apps list-deployments a21db074-3a86-449d-810f-edb0d48e45d6

# read a build log, which is where failures explain themselves
doctl apps logs a21db074-3a86-449d-810f-edb0d48e45d6 \
  --deployment <id> --type build
```

There is no rollback button. To undo a release, revert the commit on `main`
through a PR; the revert deploys like anything else.

## No Dockerfile

App Platform's native .NET buildpack detects `Platform.slnx` and handles the
SDK and the publish. Two things it imposes:

- **`SOLUTION_FILE` must be a bare filename.** It rejects anything containing
  a path separator, so the buildpack cannot be aimed at a single project — it
  publishes the whole solution. That is why `Website.Client` and
  `Website.Tests` are marked `IsPublishable=false`; see the comment in
  `Website.Client/Website.Client.csproj` for what happens otherwise.
- **Kestrel does not read `$PORT`.** The spec sets
  `ASPNETCORE_URLS=http://*:${PORT}`. Without it the container listens on 5000,
  the health check never answers, and the deploy fails after a long wait rather
  than a short one.

## DNS and TLS

Cloudflare holds the zone (`aldo`/`liv.ns.cloudflare.com`). Two records, both
**proxied**:

| Type | Name | Target |
|---|---|---|
| CNAME | `@` | `phillypause-platform-ntkmt.ondigitalocean.app` |
| CNAME | `www` | `phillypause-platform-ntkmt.ondigitalocean.app` |

Cloudflare flattens the apex CNAME, so `@` works. Because the records are
proxied, **Cloudflare terminates TLS** with its own certificate and the site
gets Cloudflare's CDN and DDoS protection. App Platform's own Let's Encrypt
certificate may sit pending; nothing reaches DigitalOcean except through
Cloudflare, so it does not matter.

Two things to keep true:

- **SSL/TLS mode must be Full or Full (strict), not Flexible.** On Flexible,
  Cloudflare talks to App Platform over plain HTTP while `Program.cs` issues an
  HTTPS redirect in production, which is the classic redirect loop.
- `Program.cs` honours `X-Forwarded-Proto`, which is what makes that redirect
  see the real scheme through two proxies rather than looping.

## ManyHands

ManyHands is served by this same component, so the spec has one ingress rule
and always will (PLAN.md §2.2). It is off in production: the spec sets no
`ManyHands__Enabled`, and no database is attached. Switching it on needs a
managed Postgres, its connection string as `ConnectionStrings__ManyHands`,
and the flag — [`MANYHANDS.md`](MANYHANDS.md) §5.

## Tokens

`doctl` needs an App Platform token with `app:create`, `app:read` and
`app:update`. No `domain:*` scopes: DNS lives at Cloudflare, not DigitalOcean.
Generate it inside the team that owns the app — a token is scoped to its team,
and apps do not move between teams.
