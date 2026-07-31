// Copyright © 2026 Krzysztof Kasprowicz

using System.Globalization;
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

if (runsIntegrationTests)
{
    // A real IMAP server, because the claim the suite exists to prove — that synchronization never marks mail as read —
    // is about what MailKit puts on the wire, and a substituted port cannot observe a server's flag state. GreenMail is
    // built for this: it is Apache-2.0, it seeds through ordinary SMTP delivery, and it answers UID SEARCH, BODY.PEEK,
    // STORE, EXPUNGE, and folder creation, so the flag can be read back over a second connection the adapter knows
    // nothing about. Its ports are GreenMail's own test offsets, and the container is named under the ephemeral prefix
    // for the same reason the PostgreSQL one is.
    //
    // Only the two protocols the suite speaks are started, plus the API server, whose readiness endpoint is what makes
    // this resource reach Healthy instead of merely Running. Without it a test would race the server's first listener.
    builder.AddContainer(OrchestrationContract.MailServerResourceName, "greenmail/standalone", "2.1.11")
        .WithContainerName($"{OrchestrationContract.EphemeralResourceNamePrefix}-mailserver")
        .WithEnvironment(
            "GREENMAIL_OPTS",
            string.Join(
                ' ',
                "-Dgreenmail.smtp.hostname=0.0.0.0",
                "-Dgreenmail.smtp.port=3025",
                "-Dgreenmail.imap.hostname=0.0.0.0",
                "-Dgreenmail.imap.port=3143",
                "-Dgreenmail.api.hostname=0.0.0.0",
                "-Dgreenmail.api.port=8080",
                // One mailbox, whose login is the local part and whose delivery address is the whole string. Verbose
                // logging stays off on purpose: it transcribes the IMAP conversation, password included, into the
                // orchestration log.
                $"-Dgreenmail.users={OrchestrationContract.MailServerAccountUserName}:{OrchestrationContract.MailServerAccountPassword}@mailmcp.test"))
        .WithEndpoint(targetPort: 3143, scheme: "tcp", name: OrchestrationContract.MailServerImapEndpointName)
        .WithEndpoint(targetPort: 3025, scheme: "tcp", name: OrchestrationContract.MailServerSmtpEndpointName)
        .WithHttpEndpoint(targetPort: 8080, name: OrchestrationContract.MailServerApiEndpointName)
        .WithHttpHealthCheck("/api/service/readiness", endpointName: OrchestrationContract.MailServerApiEndpointName);
}

var database = postgres.AddDatabase(OrchestrationContract.DatabaseResourceName);

var mailMcpHost = builder.AddProject<Projects.Host>(OrchestrationContract.HostResourceName)
    .WithReference(database)
    .WaitFor(database);

if (runsIntegrationTests)
{
    // Nothing starts the host with the application. Most of the suite verifies classes against a real database and a
    // real mailbox, and a second MailMcp reconciling folders underneath them would make its synchronization part of
    // every one of those tests' environment. The suite starts it explicitly, from a collection ordered after all of
    // them, once nothing else is asserting on the infrastructure it would touch.
    mailMcpHost
        .WithExplicitStart()
        // Stated here rather than left to appsettings.json, because the isolation above is a promise this app model
        // makes: a default edited elsewhere must not be able to turn the started host into a synchronizing one.
        .WithEnvironment("MailSynchronization__Enabled", "false")
        // The endpoint is served under the posture worth proving end to end — a credential is required, and the origins
        // are narrowed. Leaving the permissive origin default would let a suite pass while the check was never wired in.
        .WithEnvironment("McpEndpoint__Enabled", "true")
        .WithEnvironment("McpEndpoint__Authentication", "ApiKey")
        .WithEnvironment("McpEndpoint__ApiKeys__0__Name", OrchestrationContract.McpApiKeyName)
        .WithEnvironment("McpEndpoint__ApiKeys__0__SecretReference", $"plaintext:{OrchestrationContract.McpApiKey}")
        // A second key exists to be spent. Rate limits are counted per client, so proving one is enforced means taking a
        // client to zero, and doing that to the key every other test authenticates with would make this suite's results
        // depend on the order it ran in.
        .WithEnvironment("McpEndpoint__ApiKeys__1__Name", OrchestrationContract.McpExpendableApiKeyName)
        .WithEnvironment(
            "McpEndpoint__ApiKeys__1__SecretReference",
            $"plaintext:{OrchestrationContract.McpExpendableApiKey}")
        .WithEnvironment("McpEndpoint__Cors__AllowedOrigins__0", OrchestrationContract.McpPermittedOrigin)
        // Narrowed from the product defaults for the same reason the origins are: a burst small enough to exhaust
        // deliberately is what makes the difference between a limiter that is wired in and one that is not observable.
        // The period is a second, so a spent client is whole again long before anything else in the suite runs.
        .WithEnvironment(
            "McpEndpoint__RateLimiting__TokenCapacity",
            OrchestrationContract.McpRateLimitTokenCapacity.ToString(CultureInfo.InvariantCulture))
        .WithEnvironment(
            "McpEndpoint__RateLimiting__TokensPerReplenishmentPeriod",
            OrchestrationContract.McpRateLimitTokenCapacity.ToString(CultureInfo.InvariantCulture))
        .WithEnvironment(
            "McpEndpoint__RateLimiting__ReplenishmentPeriod",
            OrchestrationContract.McpRateLimitReplenishmentPeriod);
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
