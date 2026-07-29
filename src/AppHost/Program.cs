// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

// The integration-test suite starts this same app model, so the two shapes are selected here rather than duplicated in
// a second app host: a developer's orchestration keeps its data across restarts, and a test run keeps nothing.
//
// Read from the argument list rather than from builder.Configuration, which also binds environment variables. An
// IntegrationTesting variable left in a shell or a CI environment would otherwise put an ordinary run on the fixed-name
// ephemeral database and leave its volume under the prefix the test script deletes.
var runsIntegrationTests = args.Contains(OrchestrationContract.IntegrationTestingArgument, StringComparer.Ordinal);

var postgres = builder.AddPostgres(OrchestrationContract.PostgresResourceName)
    .WithImage("pgvector/pgvector")
    .WithImageTag("0.8.2-pg17");

if (runsIntegrationTests)
{
    // Named rather than left to Aspire's random postfix, and given a volume rather than none, so that both survive a
    // killed run as something the prefix identifies. A container the test topology left behind would otherwise be
    // indistinguishable from the developer's own, and skipping the volume entirely would let the image's own VOLUME
    // declaration create an anonymous one that outlives the container under a name nobody can filter on.
    postgres
        .WithContainerName($"{OrchestrationContract.EphemeralResourceNamePrefix}-postgres")
        .WithDataVolume($"{OrchestrationContract.EphemeralResourceNamePrefix}-postgres-data");
}
else
{
    // The data volume outlives the container, so a developer keeps synchronized mail across restarts instead of paying
    // an initial IMAP synchronization every time the orchestration is stopped. Recreating the schema is therefore a
    // deliberate step — the migration resource's Reset Database command — rather than a side effect of losing the
    // container.
    postgres.WithDataVolume();
}

var database = postgres.AddDatabase(OrchestrationContract.DatabaseResourceName);

var mailMcpHost = builder.AddProject<Projects.Host>(OrchestrationContract.HostResourceName)
    .WithReference(database)
    .WaitFor(database);

if (runsIntegrationTests)
{
    // The suite verifies classes against a real database rather than the composed host, so the host resource stays in
    // the model — the migration resource is defined on it, and the connection string is issued to it — but nothing
    // starts it. Starting a second MailMcp against the test database would run its synchronization workers over the
    // data a test is asserting on.
    mailMcpHost.WithExplicitStart();
}

// Host is the startup project because it is the project resource the connection string is issued to; Infrastructure
// owns the context and the migrations. The tool resource runs dotnet-ef with the orchestration's own
// ConnectionStrings__mailmcp, which is what keeps a migration authored against the same server a running MailMcp uses.
// WaitFor is not optional here despite the parent project already declaring it. The migration resource opens its own
// connection as soon as it starts, and PostgreSQL accepts a socket before it will complete an SSL handshake, so a run
// without this waits on nothing and fails the handshake against a server that is still starting.
var migrations = mailMcpHost.AddEFMigrations(OrchestrationContract.MigrationsResourceName)
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
