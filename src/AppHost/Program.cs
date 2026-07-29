// Copyright © 2026 Krzysztof Kasprowicz

var builder = DistributedApplication.CreateBuilder(args);

// The data volume outlives the container, so a developer keeps synchronized mail across restarts instead of paying an
// initial IMAP synchronization every time the orchestration is stopped. Recreating the schema is therefore a deliberate
// step — the migration resource's Reset Database command — rather than a side effect of losing the container.
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector")
    .WithImageTag("0.8.2-pg17")
    .WithDataVolume();
var database = postgres.AddDatabase("mailmcp");

var mailMcpHost = builder.AddProject<Projects.Host>("mailmcp-host")
    .WithReference(database)
    .WaitFor(database);

// Host is the startup project because it is the project resource the connection string is issued to; Infrastructure
// owns the context and the migrations. The tool resource runs dotnet-ef with the orchestration's own
// ConnectionStrings__mailmcp, which is what keeps a migration authored against the same server a running MailMcp uses.
// WaitFor is not optional here despite the parent project already declaring it. The migration resource opens its own
// connection as soon as it starts, and PostgreSQL accepts a socket before it will complete an SSL handshake, so a run
// without this waits on nothing and fails the handshake against a server that is still starting.
var migrations = mailMcpHost.AddEFMigrations("mailmcp-migrations")
    .WithMigrationsProject(Path.Combine(builder.AppHostDirectory, "..", "Infrastructure", "Infrastructure.csproj"))
    // The namespace is deliberately left to EF's own derivation from this directory, which produces
    // MailMcp.Infrastructure.Persistence.Migrations anyway. Stating it explicitly makes EF write the model snapshot to a
    // path derived from the namespace instead of to the output directory, which buries it under src/Infrastructure/MailMcp.
    .WithMigrationOutputDirectory("Persistence/Migrations")
    .WithReference(database)
    .WaitFor(database)
    .RunDatabaseUpdateOnStart();

// Applying migrations before the host starts is what lets the host refuse to serve traffic against a schema it does not
// recognize without that refusal firing on every local run.
mailMcpHost.WaitForCompletion(migrations);

builder.Build().Run();
