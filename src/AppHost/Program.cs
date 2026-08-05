// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.AppHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = DistributedApplication.CreateBuilder(args);

// The integration-test suite starts this same app model, so the two shapes are selected here rather than duplicated in
// a second app host: a developer's orchestration keeps its data across restarts, and a test run keeps nothing.
//
// Read from the argument list rather than from builder.Configuration, which also binds environment variables. An
// IntegrationTesting variable left in a shell or a CI environment would otherwise put an ordinary run on the fixed-name
// ephemeral database and leave its volume under the prefix the test script deletes.
var runsIntegrationTests = args.Contains(OrchestrationContract.IntegrationTestingArgument, StringComparer.Ordinal);

// One identifier per run, so two suites started on one machine name different containers and volumes instead of racing
// for one name — and so the caller that started a run can remove exactly what it created. The ordinary topology names
// nothing with it and leaves it empty.
var ephemeralResourceNamePrefix = runsIntegrationTests
    ? OrchestrationContract.ResolveEphemeralResourceNamePrefix(
        builder.Configuration[OrchestrationContract.EphemeralRunIdentifierVariable])
    : string.Empty;

// Stated rather than generated, because this server exists to be developed and tested against. Aspire would otherwise
// generate the password per run and keep it stable only by persisting it, which a data volume can outlive: PostgreSQL
// applies a password when it initializes an empty data directory and never again, so the two diverge and the server
// reports an authentication failure about a database nothing was wrong with. A fixed value cannot diverge from itself,
// and it is what lets psql or a database tool connect with nothing but the host and the port.
var postgresUserName = builder.AddParameter("postgres-username", OrchestrationContract.PostgresUserName);
var postgresPassword = builder.AddParameter("postgres-password", OrchestrationContract.PostgresPassword, secret: true);

var postgres = builder
    .AddPostgres(OrchestrationContract.PostgresResourceName, postgresUserName, postgresPassword)
    .WithImage("pgvector/pgvector")
    .WithImageTag("0.8.2-pg17");

if (runsIntegrationTests)
{
    // Named rather than left to Aspire's random postfix, and given a volume rather than none, so that both survive a
    // killed run as something the prefix identifies. A container the test topology left behind would otherwise be
    // indistinguishable from the developer's own, and skipping the volume entirely would let the image's own VOLUME
    // declaration create an anonymous one that outlives the container under a name nobody can filter on.
    //
    // The run identifier inside that name is what keeps two suites on one machine apart. It also makes the volume new
    // on every run, which is what the baseline migration has to apply to for a run to prove it applies cleanly at all;
    // a volume reused across runs would quietly turn every later run into an upgrade of the first one's database.
    postgres
        .WithContainerName($"{ephemeralResourceNamePrefix}-postgres")
        .WithDataVolume($"{ephemeralResourceNamePrefix}-postgres-data");
}
else
{
    // The data volume outlives the container, so a developer keeps synchronized mail across restarts instead of paying
    // an initial IMAP synchronization every time the orchestration is stopped. Recreating the schema is therefore a
    // deliberate step — the migration resource's Reset Database command — rather than a side effect of losing the
    // container.
    //
    // The container itself outlives the run for the same reason the volume outlives the container. A session lifetime
    // removes the server on every shutdown and builds it again on the next start, which costs an image check, an
    // initialization pass, and a health wait several times a day against data that was never in question. A persistent
    // one is reattached instead, so the server a developer stops is the server they get back — and stays reachable to
    // psql or a database tool while the orchestration is not running.
    //
    // Stated through the lifetime enumeration rather than through the newer WithPersistentLifetime, which Aspire gates
    // behind ASPIREPERSISTENCE001 as evaluation-only. Taking it would mean suppressing an experimental-API diagnostic
    // to reach behavior the stable overload already expresses.
    //
    // Kept is not the same as left running, and the lifetime has no third value for that, so PersistentContainerStopper
    // below stops it during shutdown. What outlives the orchestration is then the container and its data rather than a
    // PostgreSQL process and the port it holds.
    //
    // The host port is fixed for the same reason the host's own ports are: a connection string typed into a database
    // tool once should keep working. PostgreSQL's own port is the convenient one, and Aspire publishes it on the
    // loopback address, so the server a developer reaches is not one anything else on their network can.
    postgres
        .WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent)
        .WithHostPort(5432);

    // Registered last, which is what makes its shutdown run first: a hosted service stops in reverse registration
    // order, and the orchestrator that carries out the stop is registered while the builder is created.
    builder.Services.AddHostedService(provider => new PersistentContainerStopper(
        provider.GetRequiredService<ResourceCommandService>(),
        [postgres.Resource],
        provider.GetRequiredService<ILogger<PersistentContainerStopper>>()));
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
        .WithContainerName($"{ephemeralResourceNamePrefix}-mailserver")
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
                $"-Dgreenmail.users={OrchestrationContract.MailServerAccountUserName}:{OrchestrationContract.MailServerAccountPassword}@mailfathom.test"))
        .WithEndpoint(targetPort: 3143, scheme: "tcp", name: OrchestrationContract.MailServerImapEndpointName)
        .WithEndpoint(targetPort: 3025, scheme: "tcp", name: OrchestrationContract.MailServerSmtpEndpointName)
        .WithHttpEndpoint(targetPort: 8080, name: OrchestrationContract.MailServerApiEndpointName)
        .WithHttpHealthCheck("/api/service/readiness", endpointName: OrchestrationContract.MailServerApiEndpointName);
}

var database = postgres.AddDatabase(OrchestrationContract.DatabaseResourceName);

var mailFathomHost = builder.AddProject<Projects.Host>(OrchestrationContract.HostResourceName)
    .WithReference(database)
    .WaitFor(database)
    // The probe listener binds every interface by default, which is what a container wants and what a developer
    // machine does not: the probes answer without a credential, and nothing on a local network has any business asking
    // them. It is not published as an Aspire endpoint either, because Aspire issues ASPNETCORE_URLS from the endpoints
    // it knows about and the host would then serve `/` and `/mcp` on the probe port as well.
    .WithEnvironment("HealthEndpoints__BindAddress", "127.0.0.1")
    // The one place MailFathom is handed a data-encryption key without an operator provisioning one. A developer
    // running this app model is not provisioning a deployment, and a key generated by hand before the first run would
    // be ceremony bought for nothing against a database of synthetic mail — while generating one per run is the shape
    // ADR 0005 refuses, because a key that diverges from the volume it protects leaves every sealed row unopenable.
    //
    // A plaintext reference rather than a file or a credential, so the value is visible as what it is: a constant this
    // repository publishes, not a secret anything is being asked to keep.
    .WithEnvironment("DataEncryption__ActiveKeyId", OrchestrationContract.DataEncryptionKeyId)
    .WithEnvironment("DataEncryption__Keys__0__KeyId", OrchestrationContract.DataEncryptionKeyId)
    .WithEnvironment("DataEncryption__Keys__0__Material__Name", OrchestrationContract.DataEncryptionKeyName)
    .WithEnvironment(
        "DataEncryption__Keys__0__Material__SecretReference",
        $"plaintext:{OrchestrationContract.DataEncryptionKeyMaterial}");

// Passed through from this process's own environment rather than set here, and only when a developer set it. OpenSSL
// reads it while it initializes, so it is the one way to reach a mail server whose cipher suite or key size the
// platform's TLS policy refuses — and it relaxes that policy for every connection the host makes, the database
// included. Which is exactly why the app model must not be the thing that decides it applies: it carries the value an
// operator chose, or nothing at all.
//
// Never under the integration-test topology. That suite proves what MailFathom does against servers it starts itself,
// and a policy inherited from whichever machine ran it would make the handshakes it exercises depend on that machine.
var openSslConfigurationPath = Environment.GetEnvironmentVariable(OrchestrationContract.OpenSslConfigurationVariable);

if (!runsIntegrationTests && !string.IsNullOrWhiteSpace(openSslConfigurationPath))
{
    mailFathomHost.WithEnvironment(OrchestrationContract.OpenSslConfigurationVariable, openSslConfigurationPath);
}

if (runsIntegrationTests)
{
    // Nothing starts the host with the application. Most of the suite verifies classes against a real database and a
    // real mailbox, and a second MailFathom reconciling folders underneath them would make its synchronization part of
    // every one of those tests' environment. The suite starts it explicitly, from a collection ordered after all of
    // them, once nothing else is asserting on the infrastructure it would touch.
    mailFathomHost
        .WithExplicitStart()
        // The probe listener, on a port the orchestration allocates rather than on the one the host defaults to. This
        // topology runs two MailFathom processes on one machine and a default is the same number in both, so the second
        // to start would fail to bind and exit — which reaches a test as a request that never answers rather than as a
        // host that never started. Allocated for the reason every other port here is: nothing this suite opens may
        // depend on a number another run, or another host, already holds.
        //
        // Its scheme is tcp rather than http, for the reason the pinned topology's is: an http endpoint would join
        // ASPNETCORE_URLS and make the probe port an application listener, which the host refuses.
        .WithEndpoint(name: "health", scheme: "tcp", env: "HealthEndpoints__Port")
        // The MCP endpoint's socket, allocated for the reason the probe port is and declared with the tcp scheme for
        // the reason every endpoint on this resource is. It used to arrive from the project's launch profile through
        // ASPNETCORE_URLS; the host refuses that variable now, so the suite states the port in the section that owns it.
        .WithEndpoint(
            name: OrchestrationContract.HostHttpEndpointName,
            scheme: "tcp",
            env: "McpEndpoint__Port")
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
        // The administrative surface, served under the posture worth proving end to end: enabled, on a listener of its
        // own, and behind a credential that is none of the MCP keys above. Its port is allocated rather than defaulted,
        // for the reason the probe port is — two MailFathom processes run at once under this topology — and injected
        // into the host's own configuration key, so the number is written once rather than declared here and configured
        // again beside it. The scheme is tcp, which OrchestrationContract.HostAdminEndpointName explains.
        .WithEndpoint(name: OrchestrationContract.HostAdminEndpointName, scheme: "tcp", env: "AdminEndpoint__Port")
        .WithEnvironment("AdminEndpoint__Enabled", "true")
        .WithEnvironment("AdminEndpoint__BindAddress", OrchestrationContract.AdminEndpointBindAddress)
        .WithEnvironment("AdminEndpoint__Authentication", "ApiKey")
        .WithEnvironment("AdminEndpoint__ApiKeys__0__Name", OrchestrationContract.AdminApiKeyName)
        .WithEnvironment(
            "AdminEndpoint__ApiKeys__0__SecretReference",
            $"plaintext:{OrchestrationContract.AdminApiKey}")
        // Narrowed from the product defaults for the same reason the origins are: a burst small enough to exhaust
        // deliberately is what makes the difference between a limiter that is wired in and one that is not observable.
        // The replenishment period outlasts the run, so what a client spent stays spent and a refusal cannot depend on
        // how quickly the machine dispatched the burst.
        .WithEnvironment(
            "McpEndpoint__RateLimiting__TokenCapacity",
            OrchestrationContract.McpRateLimitTokenCapacity.ToString(CultureInfo.InvariantCulture))
        .WithEnvironment(
            "McpEndpoint__RateLimiting__TokensPerReplenishmentPeriod",
            OrchestrationContract.McpRateLimitTokenCapacity.ToString(CultureInfo.InvariantCulture))
        .WithEnvironment(
            "McpEndpoint__RateLimiting__ReplenishmentPeriod",
            OrchestrationContract.McpRateLimitReplenishmentPeriod)
        // Raised rather than narrowed, so the concurrency limit cannot refuse anything the suite sends and a 429 it
        // observes can only have come from the per-client bucket the route's policy carries.
        .WithEnvironment(
            "McpEndpoint__RateLimiting__MaxConcurrentRequests",
            OrchestrationContract.McpRateLimitMaxConcurrentRequests.ToString(CultureInfo.InvariantCulture));
}
else
{
    // The two sockets a developer's run serves, each stated by the section that owns it.
    mailFathomHost
        // The MCP endpoint's own socket, stated to the app model and injected into the host's own configuration key, so
        // the number is written once rather than declared here and configured again beside it. Its scheme is tcp rather
        // than http, for the reason the probe endpoint's is: Aspire builds ASPNETCORE_URLS from the http and https
        // endpoints, and MailFathom refuses that variable outright — every surface states where it is served in its own
        // section. A tcp endpoint is recorded and published without reaching it; what a client connects with is still
        // HTTP.
        //
        // Fixed rather than allocated, and bound by the host itself rather than by a proxy in front of it. An MCP
        // client's configuration names an address once, so a port that moved with a launch profile or with the
        // orchestrator's own allocation would make that address a per-run detail; unproxied also means the socket a
        // client connects to is the socket Kestrel opened, which is what keeps a TLS handshake and a client certificate
        // a conversation with the host. 8080 and 8081 are the numbers the container image already publishes, so a local
        // run and a deployed one answer on the same ports.
        //
        // Only the ordinary topology pins them. The integration suite starts this same app model, and a fixed port
        // there would let one run refuse to bind because another still holds it.
        //
        // No HTTPS endpoint accompanies this one. Kestrel serves an https:// address it was handed with no endpoint
        // configuration out of the ASP.NET Core development certificate, and MailFathom never serves a listener out of
        // one — a developer who wants TLS locally configures McpEndpoint:Https the way a deployment does, which is also
        // the shape they will ship.
        .WithEndpoint(
            name: OrchestrationContract.HostHttpEndpointName,
            scheme: "tcp",
            port: 8080,
            targetPort: 8080,
            isProxied: false,
            env: "McpEndpoint__Port")
        // The probe listener, declared here so that both ports are read in one place and the orchestrator shows the one
        // a developer curls.
        //
        // WithHttpHealthCheck is unavailable for the reason the scheme above is tcp: it derives its address from an
        // http endpoint, and this app model declares none.
        .WithEndpoint(
            name: "health",
            scheme: "tcp",
            port: 8081,
            targetPort: 8081,
            isProxied: false,
            env: "HealthEndpoints__Port");
}

// Host is the startup project because it is the project resource the connection string is issued to; Infrastructure
// owns the context and the migrations. The tool resource runs dotnet-ef with the orchestration's own
// ConnectionStrings__mailfathom, which is what keeps a migration authored against the same server a running MailFathom uses.
// WaitFor is not optional here despite the parent project already declaring it. The migration resource opens its own
// connection as soon as it starts, and PostgreSQL accepts a socket before it will complete an SSL handshake, so a run
// without this waits on nothing and fails the handshake against a server that is still starting.
var migrations = mailFathomHost.AddEFMigrations(OrchestrationContract.MigrationsResourceName)
    .WithMigrationsProject(Path.Combine(builder.AppHostDirectory, "..", "Infrastructure", "Infrastructure.csproj"))
    // The namespace is deliberately left to EF's own derivation from this directory, which produces
    // MailFathom.Infrastructure.Persistence.Migrations anyway. Stating it explicitly makes EF write the model snapshot to a
    // path derived from the namespace instead of to the output directory, which buries it under src/Infrastructure/MailFathom.
    .WithMigrationOutputDirectory("Persistence/Migrations")
    .WithReference(database)
    .WaitFor(database)
    .RunDatabaseUpdateOnStart()
    // The deployment artifact, written to `efmigrations/` under the publish output and executed by nothing: `aspire
    // publish` produces the SQL an operator reads, takes a backup against, and runs deliberately. Idempotent, so the
    // one artifact answers both the empty database and the one already carrying part of the chain — the operator does
    // not have to know which migrations a given database holds to know which script to apply.
    //
    // Transactions are kept, which is the whole reason a failed apply leaves the database on the migration it was on
    // rather than half way through one. EF wraps each migration in its own transaction, so a chain that fails at the
    // third migration keeps the first two; PostgreSQL runs DDL transactionally, which is what makes that true here and
    // is why ScriptNoTransactions would be a loss rather than a portability concession.
    .PublishAsMigrationScript(idempotent: true);

// Applying migrations before the host starts is what lets the host refuse to serve traffic against a schema it does not
// recognize without that refusal firing on every local run.
mailFathomHost.WaitForCompletion(migrations);

if (runsIntegrationTests)
{
    // A second MailFathom, served over HTTPS behind mutual TLS, because a client certificate exists only on a TLS
    // connection this process terminated and nothing above serves one. Whether a certificate is required is one answer
    // for a whole process, so this cannot be a posture applied to the host above: that host is reached without a
    // certificate by every test written about a credential, an origin, or a limiter, and a Required profile there would
    // refuse all of them.
    //
    // No launch profile, so the listeners come from the endpoint declared here rather than from applicationUrl — two
    // resources built from the same project would otherwise ask for the same fixed ports. The HTTPS profile then binds
    // the port this endpoint allocated, which is what keeps the address the suite connects to and the socket the host
    // opens one number rather than two.
    var mutualTlsHost = builder
        .AddProject<Projects.Host>(OrchestrationContract.MutualTlsHostResourceName, launchProfileName: null)
        .WithEndpoint(name: OrchestrationContract.MutualTlsHostHttpsEndpointName, scheme: "tcp")
        // Its own probe listener, allocated for the reason the host above allocates one: both processes run at once
        // under this topology, and the port a host defaults to is the same number in each. On loopback for that host's
        // reason as well — the probes answer without a credential, and the machine a suite runs on is a developer's as
        // often as it is a runner's. The binding is restated rather than inherited: it is set on the resource above,
        // and this is a second resource that shares nothing but the project it is built from.
        .WithEndpoint(name: "health", scheme: "tcp", env: "HealthEndpoints__Port")
        .WithEnvironment("HealthEndpoints__BindAddress", "127.0.0.1")
        .WithReference(database)
        .WaitFor(database)
        .WithExplicitStart()
        .WithEnvironment("MailSynchronization__Enabled", "false")
        .WithEnvironment("McpEndpoint__Enabled", "true")
        // Deliberately unauthenticated: what this host exists to prove is which certificate the endpoint judges, and a
        // credential in front of that would make every refusal answerable by two controls instead of one.
        .WithEnvironment("McpEndpoint__Authentication", "None")
        .WithEnvironment("McpEndpoint__Https__Endpoints__0__Name", "integration-tests")
        .WithEnvironment("McpEndpoint__Https__Endpoints__0__Domain", OrchestrationContract.MutualTlsHostDomain)
        .WithEnvironment("McpEndpoint__Https__Endpoints__0__BindAddress", OrchestrationContract.MutualTlsHostBindAddress)
        .WithEnvironment(
            "McpEndpoint__Https__Endpoints__0__ServerCertificate__CertificateChain__Name",
            "integration-tests-server-certificate")
        .WithEnvironment(
            "McpEndpoint__Https__Endpoints__0__ServerCertificate__CertificateChain__SecretReference",
            $"env:{OrchestrationContract.MutualTlsServerCertificateChainVariable}")
        .WithEnvironment(
            "McpEndpoint__Https__Endpoints__0__ServerCertificate__PrivateKey__Name",
            "integration-tests-server-private-key")
        .WithEnvironment(
            "McpEndpoint__Https__Endpoints__0__ServerCertificate__PrivateKey__SecretReference",
            $"env:{OrchestrationContract.MutualTlsServerPrivateKeyVariable}")
        .WithEnvironment(
            "McpEndpoint__ClientCertificateProfiles__0__Name",
            OrchestrationContract.MutualTlsClientProfileName)
        // Required, so a request presenting no certificate is refused by the endpoint rather than served. That refusal
        // is the claim only a real handshake can carry: it arrives as an HTTP response, which means the connection was
        // established for a client that had nothing to present.
        .WithEnvironment("McpEndpoint__ClientCertificateProfiles__0__Requirement", "Required")
        .WithEnvironment(
            "McpEndpoint__ClientCertificateProfiles__0__TrustAnchors__0__Name",
            "integration-tests-client-authority")
        .WithEnvironment(
            "McpEndpoint__ClientCertificateProfiles__0__TrustAnchors__0__SecretReference",
            $"env:{OrchestrationContract.MutualTlsClientTrustAnchorVariable}")
        .WithEnvironment(
            "McpEndpoint__ClientCertificateProfiles__0__SubjectAlternativeNames__0",
            OrchestrationContract.MutualTlsClientDnsName);

    mutualTlsHost.WithEnvironment(
        "McpEndpoint__Https__Endpoints__0__Port",
        mutualTlsHost
            .GetEndpoint(OrchestrationContract.MutualTlsHostHttpsEndpointName)
            .Property(EndpointProperty.TargetPort));

    mutualTlsHost.WaitForCompletion(migrations);
}

builder.Build().Run();
