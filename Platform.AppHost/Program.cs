using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// Postgres in Docker, ephemeral: no data volume, so every run starts from
// nothing, applies the schema and seeds the fictional data. That is what lets
// 0001_schema.sql be edited in place until the first deploy (MANYHANDS.md §4):
// a local database never needs upgrading, only recreating.
// 17, the version ManyHands.Tests runs against, so local and CI agree.
var postgres = builder.AddPostgres("postgres").WithImageTag("17-alpine");

// Named for the connection string the site reads: ConnectionStrings:ManyHands.
var manyHands = postgres.AddDatabase("ManyHands");

// The site, with ManyHands on and seeded, on the same port as a plain
// "dotnet run" (launchSettings' http profile), so every link in the logged
// emails points back at it.
var website = builder.AddProject<Projects.Website_Portal>("website", launchProfileName: "http")
    .WithReference(manyHands)
    .WaitFor(manyHands)
    .WithEnvironment("ManyHands__Enabled", "true")
    .WithEnvironment("ManyHands__Seed", "true")
    .WithExternalHttpEndpoints();

// Aspire does not hand the AppHost's own configuration to a project, so the
// sections the site reads are passed through: set one in the AppHost's user
// secrets and the site has it on the next run.
//   dotnet user-secrets set --project Platform.AppHost "ManyHands:Admins:0" "you@example.org"
//   dotnet user-secrets set --project Platform.AppHost "Email:ApiKey" "re_..."
// Only what is set travels, so the site's own defaults stay the single place a
// default is decided. ManyHands:Enabled and :Seed are fixed above.
foreach (var section in new[] { "ManyHands", "Email", "Discord", "FederalOfficials" })
{
    foreach (var (key, value) in builder.Configuration.GetSection(section).AsEnumerable())
    {
        if (string.IsNullOrEmpty(value) || key is "ManyHands:Enabled" or "ManyHands:Seed") continue;
        website.WithEnvironment(key.Replace(":", "__"), value);
    }
}

builder.Build().Run();
