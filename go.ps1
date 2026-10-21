#Requires -Version 5.1
<#
    go.ps1 -- one command to pick this repo up on any machine, then run the site.

    Everything here is per-machine state that a clone cannot carry: the commit
    identity, which GitHub account pushes, and the SDK check. Each step looks
    before it changes anything, so running this twice is the same as running it
    once. Run it after every pull if you like; it costs nothing.

        .\go.ps1            set the machine up, then run the site
        .\go.ps1 -NoRun     set the machine up and stop

    See README, "Resuming on another machine".
#>
[CmdletBinding()]
param(
    # Do the setup and skip `dotnet run` -- for CI-ish or scripted use.
    [switch] $NoRun
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$CommitName  = 'Louis S. Berman'
$CommitEmail = 'louis@phillypause.org'
$GitHubLogin = 'louis-phillypause'

function Step($text) { Write-Host "  $text" }
function Warn($text) { Write-Host "  $text" -ForegroundColor Yellow }
function Fail($text) { Write-Host "  $text" -ForegroundColor Red }

Write-Host ""
Write-Host "PhillyPause/Platform" -ForegroundColor Cyan

# ---------------------------------------------------------------- 1. identity
# Repo-local on purpose. A global identity would follow you into unrelated
# work, and the wrong author email on a commit is invisible until it is in the
# history.
if ((git config --local user.name) -ne $CommitName) {
    git config --local user.name $CommitName
}
if ((git config --local user.email) -ne $CommitEmail) {
    git config --local user.email $CommitEmail
}
Step "commits as $CommitName <$CommitEmail>"

# ------------------------------------------------------------- 2. credentials
# Git Credential Manager may hold a different GitHub account for this machine,
# and a push would then authenticate as the wrong one. Ask the gh CLI instead,
# for this repo only.
git config --local 'credential.https://github.com.helper' '' | Out-Null
git config --local --add 'credential.https://github.com.helper' '!gh auth git-credential'
Step "pushes authenticate through the gh CLI"

# -------------------------------------------------------------- 3. gh account
$ghOk = $true
$who = $null
try { $who = (gh api user --jq .login 2>$null) } catch { }

if (-not $who) {
    $ghOk = $false
    Warn "gh is not logged in. Run these two, then .\go.ps1 again:"
    Write-Host "      gh auth login"
    Write-Host "      gh auth refresh -h github.com -s workflow"
}
else {
    if ($who -ne $GitHubLogin) {
        Warn "gh is logged in as '$who', not '$GitHubLogin'."
        Warn "That account may not be able to push to the org. To switch:"
        Write-Host "      gh auth switch --user $GitHubLogin"
    }
    else {
        Step "gh account $who"
    }

    # The repo contains .github/workflows/ci.yml, and GitHub refuses any push
    # that touches a workflow file unless the token carries the workflow scope.
    $scopes = ''
    try {
        $scopes = (gh api -i user 2>$null |
            Select-String -Pattern '^X-Oauth-Scopes:' |
            ForEach-Object { $_.Line }) -join ''
    } catch { }

    if ($scopes -and $scopes -notmatch 'workflow') {
        $ghOk = $false
        Warn "token is missing the 'workflow' scope, so a push that touches"
        Warn ".github/workflows/ will be rejected. Fix with:"
        Write-Host "      gh auth refresh -h github.com -s workflow"
    }
}

# ---------------------------------------------------------------- 4. the SDK
$sdk = $null
try { $sdk = (dotnet --version 2>$null) } catch { }

if (-not $sdk) {
    Fail "no .NET SDK on PATH. Install .NET 10: https://dotnet.microsoft.com/download"
    exit 1
}
if ($sdk -notmatch '^10\.') {
    Warn "dotnet $sdk is on PATH; this solution targets net10.0."
}
else {
    Step "dotnet $sdk"
}

# ------------------------------------------------------------------- 5. go
Write-Host ""
if (-not $ghOk) {
    Warn "Setup incomplete -- see above. The site will still run locally."
    Write-Host ""
}

if ($NoRun) {
    Step "ready. Run everything with: dotnet run --project Platform/Platform.AppHost (needs Docker)"
    Write-Host ""
    exit 0
}

Write-Host "Starting the site. Ctrl+C to stop." -ForegroundColor Cyan
Write-Host ""
dotnet run --project Website/Website.Portal
