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
builder.AddProject<Projects.Website_Portal>("website", launchProfileName: "http")
    .WithReference(manyHands)
    .WaitFor(manyHands)
    .WithEnvironment("ManyHands__Enabled", "true")
    .WithEnvironment("ManyHands__Seed", "true")
    .WithExternalHttpEndpoints();

builder.Build().Run();
