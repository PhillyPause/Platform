using ManyHands.Data;
using Microsoft.AspNetCore.HttpOverrides;
using Website.Portal.Components;
using Website.Portal.Volunteer;
using Website.Content;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// Content is read and rendered once, at startup. A request never touches the
// disk or the Markdown parser (PLAN.md 2.3).
builder.Services.AddSingleton(new ContentStore(
    Path.Combine(AppContext.BaseDirectory, "Content")));

// ManyHands, the volunteer system, is off unless ManyHands:Enabled is set.
// Off, nothing of it is registered and the site needs no database; the home
// page never touches one either way (Docs/MANYHANDS.md).
var manyHandsEnabled = builder.Configuration.IsManyHandsEnabled();
if (manyHandsEnabled)
{
    builder.Services.AddManyHands(builder.Configuration);
    builder.Services.AddSingleton<FormThrottle>();
}

// Registered either way, so the volunteer pages' [Authorize] has something to
// answer to. Nothing issues the cookie unless a volunteer signs in, and with
// ManyHands off nobody can.
builder.Services.AddVolunteerSession(builder.Environment.IsDevelopment());

// App Platform terminates TLS and fronts this container, so without honouring
// X-Forwarded-Proto every request looks like plain HTTP and the redirect below
// loops. The container is reachable only through the platform router, so
// trusting the immediate proxy is right. A reader's own address comes from
// Cloudflare's header instead (FormThrottle.ClientAddress).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

if (manyHandsEnabled)
{
    // Before the first request, and fatal on failure: one instance, so
    // nothing else can be migrating at the same time.
    app.Services.MigrateManyHands();
}

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseForwardedHeaders();

// Only in production: locally the site runs on plain HTTP by default, and
// redirecting with no HTTPS port bound just logs a warning on every request.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// After the status-code pages, so a gated path renders the ordinary 404.
app.UseManyHandsGate(manyHandsEnabled);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

if (manyHandsEnabled)
{
    app.MapVolunteerEndpoints();
}
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Website.Client._Imports).Assembly);

app.Run();

/// <summary>Exposed so tests can drive the real pipeline.</summary>
public partial class Program;
