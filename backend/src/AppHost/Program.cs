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
var runsIntegrationTests = OrchestrationContract.RunsIntegrationTests(args);

// The port this checkout pinned for a socket, or null where it pinned none — which is what leaves the number to be
// found per run and lets a second checkout start while the first is running. Read from the app host's own
// configuration, so `dotnet user-secrets --project backend/src/AppHost/AppHost.csproj` is where a developer states one; unlike
// the topology switch above, an ambient value here can only move a port the run publishes, never select a topology.
int? PinnedPort(string configurationKey) =>
    OrchestrationContract.ResolvePinnedPort(configurationKey, builder.Configuration[configurationKey]);

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
    .WithImageTag("0.8.6-pg18");

// Mounted explicitly rather than through WithDataVolume, which derives this path by parsing a PostgreSQL major version
// out of the image tag: it takes everything before the first `-` — `0.8.6` on a pgvector tag shaped `0.8.6-pg18` — and
// reads the major component of that, which is `0`, so the version test never sees 18 and it falls back to the pre-18
// `/var/lib/postgresql/data`. PostgreSQL 18 moved the image's data directory under a
// version-specific subdirectory and moved the declared volume up to the parent, so a volume mounted at the old path
// would hold nothing and the database would live in the container's writable layer — lost with the container rather
// than kept by the volume that was asked for.
const string postgresDataDirectory = "/var/lib/postgresql";

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
        .WithVolume($"{ephemeralResourceNamePrefix}-postgres-data", postgresDataDirectory);
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
    // The host port is allocated for the same reason the host's own ports are: a fixed one is a single port, and a
    // second checkout running its own orchestration cannot have it. A connection string typed into a database tool once
    // should still be able to keep working, so a developer who wants that pins the conventional 5432 under
    // OrchestrationContract.PinnedPostgresPortKey and gets it on every run that reads it. Whatever the number is,
    // Aspire publishes it on the loopback address, so the server a developer reaches is not one anything else on their
    // network can.
    //
    // The volume keeps the name WithDataVolume would have given it, which is what makes it still one volume per
    // checkout: the generated name carries a hash of the app host's path, so a clone and a worktree own different
    // databases instead of racing for one. Only the path it is mounted at is stated here, for the reason above.
    postgres
        .WithVolume(VolumeNameGenerator.Generate(postgres, "data"), postgresDataDirectory)
        .WithLifetime(ContainerLifetime.Persistent)
        .WithHostPort(PinnedPort(OrchestrationContract.PinnedPostgresPortKey));

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

    // A real Presidio analyzer, because the claim the suite exists to prove about the personal-data scanner is that the
    // image an operator pulls answers the request MailFathom builds with the entities MailFathom expects, at the offsets
    // it expects them: a scripted handler proves the mapping works on the payload somebody hand-wrote. An ordinary
    // container resource under the ephemeral prefix, like PostgreSQL and the mail server, rather than a fixture of its
    // own — the suite starts the orchestration a developer runs and defines no container topology beside it.
    //
    // The image is the one the deployment assets name, at the same pin, so what the suite exercises is what an operator
    // gets. Its health route is what makes the resource reach Healthy instead of merely Running, which matters more here
    // than anywhere else in this topology: the container loads a language model before it serves anything, so a test that
    // waited only for the container would race that load and read it as an analyzer that answered nothing.
    builder
        .AddContainer(
            OrchestrationContract.PersonalDataAnalyzerResourceName,
            "ghcr.io/data-privacy-stack/presidio-analyzer",
            "2.2.364")
        .WithContainerName($"{ephemeralResourceNamePrefix}-presidio-analyzer")
        .WithHttpEndpoint(
            targetPort: OrchestrationContract.PersonalDataAnalyzerContainerPort,
            name: OrchestrationContract.PersonalDataAnalyzerEndpointName)
        .WithHttpHealthCheck("/health", endpointName: OrchestrationContract.PersonalDataAnalyzerEndpointName);

    // A real Apache SpamAssassin daemon, for the reason the analyzer above is real: what the suite exists to prove
    // about the spam scanner is that the image an operator pulls answers the request MailFathom builds in the shape
    // MailFathom parses, and a scripted daemon proves the parser handles the payload somebody hand-wrote. An ordinary
    // container resource under the ephemeral prefix rather than a fixture of its own.
    //
    // The image is the one the deployment assets name, at the same digest, so what the suite exercises is what an
    // operator gets — including the switch that keeps it from resolving blocklists, which is the deployment's posture
    // rather than the image's default in every shape but this one's.
    //
    // No health check is declared, unlike every other resource here, and it is the protocol that decides that: the
    // daemon speaks its own line protocol on a TCP port, so there is no route to probe and no command Aspire could
    // compose. The suite waits for the daemon's own readiness command instead, which it can issue because it speaks
    // that protocol.
    // An S3-compatible endpoint, for the reason the two above are real servers: what the suite exists to prove about
    // the object backend is that a payload written under a minted key comes back byte for byte over the protocol, and
    // a substituted client proves only that MailFathom composed the request it meant to. An ordinary container
    // resource under the ephemeral prefix rather than a fixture of its own.
    //
    // Silo rather than a test double, and the difference is the whole point of the resource: a mock accepts the
    // requests it was written to accept, so what it proves is that MailFathom composed the request it meant to. Silo
    // is a maintained fork of the open-source MinIO server, keeping one release line alive after upstream ended
    // community distribution, and it is a server an operator can actually deploy — so a request it rejects is a defect
    // MailFathom would have shipped against every vendor. What it settles is the exchange: the signed request,
    // path-style addressing against a custom endpoint, the conditional write §2 of ADR 0017 rests on, the digest the
    // endpoint agrees it received, the listing reclamation pages through, and the bytes.
    //
    // It is AGPL-3.0-or-later, which the acceptance policy in `THIRD_PARTY_LICENSES.md` places behind the owner's
    // explicit approval; issue #1131 is that approval, and the register carries the reading it was granted under — a
    // separate process reached over the network, pulled from its own registry, with nothing vendored, linked, or
    // redistributed here.
    //
    // The credential below is the root credential the server is initialized with rather than a value it ignores, so
    // the suite's requests are signed and checked rather than merely well formed.
    //
    // No volume, unlike PostgreSQL: nothing here outlives a run, and an endpoint that kept objects between runs would
    // let a test read one an earlier run wrote. The bucket is created by the fixture, because the server ships none
    // and creating one is a request the S3 API already answers.
    builder
        .AddContainer(
            OrchestrationContract.ObjectStorageResourceName,
            "docker.io/pgsty/silo",
            "RELEASE.2026-08-06T00-00-00Z")
        .WithContainerName($"{ephemeralResourceNamePrefix}-object-storage")
        .WithEnvironment("MINIO_ROOT_USER", OrchestrationContract.ObjectStorageAccessKey)
        .WithEnvironment("MINIO_ROOT_PASSWORD", OrchestrationContract.ObjectStorageSecretKey)
        // The server's own start command, which every MinIO-derived image takes as arguments rather than as
        // configuration. One directory inside the container, because a single-node single-drive pool is the topology a
        // run needs and the only one a container with no volume could keep anything in.
        .WithArgs("server", OrchestrationContract.ObjectStorageDataDirectory)
        .WithHttpEndpoint(
            targetPort: OrchestrationContract.ObjectStorageContainerPort,
            name: OrchestrationContract.ObjectStorageEndpointName)
        // The server's own liveness route, which answers before any bucket exists — every other route on this port is
        // a request about a bucket, and the root answers `403` to an unsigned one. A 200 from it means the pool is
        // formatted and the API is serving rather than starting.
        .WithHttpHealthCheck(
            "/minio/health/live",
            endpointName: OrchestrationContract.ObjectStorageEndpointName);

    builder
        .AddContainer(
            OrchestrationContract.SpamScannerResourceName,
            "docker.io/axllent/spamassassin")
        .WithImageSHA256("9bea393891c92a3531cb7081e9b2c478a654a048c9a6fa6f9d1df4300bf3ab8b")
        .WithContainerName($"{ephemeralResourceNamePrefix}-spamassassin")
        .WithEnvironment("DNS_CHECKS", "0")
        .WithEndpoint(
            targetPort: OrchestrationContract.SpamScannerContainerPort,
            scheme: "tcp",
            name: OrchestrationContract.SpamScannerEndpointName);
}

var database = postgres.AddDatabase(OrchestrationContract.DatabaseResourceName);

var mailFathomHost = builder.AddProject<Projects.Host>(OrchestrationContract.HostResourceName)
    .WithReference(database)
    .WaitFor(database)
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

// The normal local topology favors compatibility with older mail servers through the repository's supported
// security-level-1 policy. It still accepts an explicit path for calibration, while integration tests inherit neither.
var openSslConfigurationPath = OrchestrationContract.ResolveOpenSslConfigurationPath(
    runsIntegrationTests,
    Environment.GetEnvironmentVariable(OrchestrationContract.OpenSslConfigurationVariable),
    AppContext.BaseDirectory);

if (openSslConfigurationPath is not null)
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
        // The MCP endpoint's socket, on a port the orchestration allocates rather than on the one the host defaults to.
        // This topology runs two MailFathom processes on one machine and a default is the same number in both, so the
        // second to start would fail to bind and exit — which reaches a test as a request that never answers rather
        // than as a host that never started. Nothing this suite opens may depend on a number another run, or another
        // host, already holds.
        //
        // Its scheme is tcp rather than http, because an http endpoint would join ASPNETCORE_URLS and make this an
        // application listener, which the host refuses. The socket used to arrive from the project's launch profile
        // through that variable; the suite states the port in the section that owns it instead.
        .WithEndpoint(
            name: OrchestrationContract.HostHttpEndpointName,
            scheme: "tcp",
            env: "McpEndpoint__Port")
        // The probes are served on that same socket rather than on one of their own, which is the arrangement a
        // single-node deployment publishes and the only place anything proves it works. What the composition decides is
        // invisible from configuration: one Kestrel listener serves the union of what the two surfaces answer, so a
        // probe is answered there without a credential while the MCP route on it still requires one, and a path
        // belonging to a surface this socket does not serve is refused rather than served by whichever route matches.
        //
        // Both sections state the same bind address for that reason. They describe one socket, and an address written
        // in one place and defaulted in the other would be two sockets the host refuses to open — a disagreement it
        // reports at startup, which reaches this suite as a host that never started.
        .WithEnvironment("McpEndpoint__BindAddress", OrchestrationContract.ComposedHostBindAddress)
        .WithEnvironment("HealthEndpoints__BindAddress", OrchestrationContract.ComposedHostBindAddress)
        // Stated here rather than left to appsettings.json, because the isolation above is a promise this app model
        // makes: a default edited elsewhere must not be able to turn the started host into a synchronizing one.
        .WithEnvironment("MailSynchronization__Enabled", "false")
        // The one account this host serves, which is the account the suite stores its mail under. Configuration is what
        // defines the served set, and it is read whether or not synchronization runs: an operator who switched
        // synchronization off has not asked for the copy already stored to become unreadable. So this is what lets a
        // tool call over the MCP endpoint answer from mail rather than from an empty scope. Nothing below reaches a
        // server either — the reading endpoint is absent for that reason, and the delivery block further down is read
        // for the address it declares rather than connected to.
        .WithEnvironment(
            "MailSynchronization__Accounts__0__AccountId",
            OrchestrationContract.ServedMailAccountId)
        // Required configuration, so the account carries it whether or not anything reads it back: a host missing it
        // fails startup, and the topology would then be unreachable rather than merely unnamed.
        .WithEnvironment(
            "MailSynchronization__Accounts__0__DisplayName",
            OrchestrationContract.ServedMailAccountDisplayName)
        // The folder that account maps, which is what makes the mail stored in it readable through a tool. A readable
        // scope is composed from mappings rather than from what the store holds, so an account with no folder answers a
        // tool call with an empty window rather than with an unnarrowed one — and the remote path is stated because a
        // mapping names a folder on a server, even under a host whose synchronization is switched off and which
        // therefore never reaches one.
        .WithEnvironment(
            "MailSynchronization__Accounts__0__Folders__0__Alias",
            OrchestrationContract.ComposedHostReadableFolderAlias)
        .WithEnvironment(
            "MailSynchronization__Accounts__0__Folders__0__RemotePath",
            OrchestrationContract.ComposedHostReadableFolderAlias)
        // The login the account is reached under, which the delivery block below authenticates as and which its
        // validation requires. It is a mailbox address rather than a bare name, so the account states one identity
        // whether a reader or a sender asks for it.
        .WithEnvironment(
            "MailSynchronization__Accounts__0__UserName",
            OrchestrationContract.ComposedHostSendingAddress)
        // The submission endpoint that makes this account able to send, which is what a tool queueing a reply or a
        // forward is refused without. It names a host in the reserved testing domain rather than the orchestrated mail
        // server: what a tool call produces is a durable record, and whether that record is then delivered is the
        // outbox's own behaviour, proven against a real server by the delivery tests rather than through this host. So
        // the delivery pass here finds a host that does not resolve, defers the send under its own bounded budget, and
        // reaches no mail server at all. The port and the connection security are left at their defaults, which
        // name implicit TLS on the submission port and therefore need no opt-in from the account's transport policy.
        // Sending is off for every account until an operator turns it on, so a composed host that only declared a
        // submission endpoint would refuse every sending tool with the coded refusal that says so — and, with no record
        // ever written, would leave the tools over a queued send nothing to be asked about. Turning it on here is what
        // makes those suites exercise the contract they are about rather than the switch in front of it.
        .WithEnvironment("MailSynchronization__Accounts__0__Delivery__Enabled", "true")
        .WithEnvironment(
            "MailSynchronization__Accounts__0__Delivery__Host",
            OrchestrationContract.ComposedHostSubmissionHost)
        .WithEnvironment(
            "MailSynchronization__Accounts__0__Delivery__FromAddress",
            OrchestrationContract.ComposedHostSendingAddress)
        // Required because a submission endpoint permitting a password mechanism is validated for one at startup, and
        // spent by nothing: it is the mailbox password the ephemeral topology already declares, under the same
        // restriction, for a server this host never opens a session with.
        .WithEnvironment(
            "MailSynchronization__Accounts__0__Delivery__Secrets__Password__Name",
            OrchestrationContract.ComposedHostSubmissionPasswordName)
        .WithEnvironment(
            "MailSynchronization__Accounts__0__Delivery__Secrets__Password__SecretReference",
            $"plaintext:{OrchestrationContract.MailServerAccountPassword}")
        // The endpoint is served under the posture worth proving end to end — a credential is required, and the origins
        // are narrowed. Leaving the permissive origin default would let a suite pass while the check was never wired in.
        .WithEnvironment("McpEndpoint__Enabled", "true")
        .WithEnvironment("McpEndpoint__Authentication__0__ApiKey__Name", OrchestrationContract.McpApiKeyName)
        .WithEnvironment(
            "McpEndpoint__Authentication__0__ApiKey__SecretReference",
            $"plaintext:{OrchestrationContract.McpApiKey}")
        // A second key exists to be spent. Rate limits are counted per client, so proving one is enforced means taking a
        // client to zero, and doing that to the key every other test authenticates with would make this suite's results
        // depend on the order it ran in.
        .WithEnvironment(
            "McpEndpoint__Authentication__1__ApiKey__Name",
            OrchestrationContract.McpExpendableApiKeyName)
        .WithEnvironment(
            "McpEndpoint__Authentication__1__ApiKey__SecretReference",
            $"plaintext:{OrchestrationContract.McpExpendableApiKey}")
        .WithEnvironment("McpEndpoint__Cors__AllowedOrigins__0", OrchestrationContract.McpPermittedOrigin)
        // The administrative surface, served under the posture worth proving end to end: enabled, on a listener of its
        // own, and behind a credential that is none of the MCP keys above. A socket of its own rather than the shared
        // one above, so that the suite carries both arrangements at once: what a shared socket serves, and what one it
        // does not serve refuses. Its port is allocated rather than defaulted, for the reason the MCP port is — two
        // MailFathom processes run at once under this topology — and injected into the host's own configuration key, so
        // the number is written once rather than declared here and configured again beside it. The scheme is tcp, which
        // OrchestrationContract.HostAdminEndpointName explains.
        .WithEndpoint(name: OrchestrationContract.HostAdminEndpointName, scheme: "tcp", env: "AdminEndpoint__Port")
        .WithEnvironment("AdminEndpoint__Enabled", "true")
        .WithEnvironment("AdminEndpoint__BindAddress", OrchestrationContract.AdminEndpointBindAddress)
        .WithEnvironment("AdminEndpoint__Authentication__0__ApiKey__Name", OrchestrationContract.AdminApiKeyName)
        .WithEnvironment(
            "AdminEndpoint__Authentication__0__ApiKey__SecretReference",
            $"plaintext:{OrchestrationContract.AdminApiKey}")
        // A second entry, narrowed to one permission. The entry above writes no grant and therefore reaches every
        // administrative route, so it is the arrangement under which a route's published permission decides nothing a
        // caller can observe; this one is what makes the enforcement visible from where an operator stands.
        .WithEnvironment(
            "AdminEndpoint__Authentication__1__ApiKey__Name",
            OrchestrationContract.AdminNarrowedApiKeyName)
        .WithEnvironment(
            "AdminEndpoint__Authentication__1__ApiKey__SecretReference",
            $"plaintext:{OrchestrationContract.AdminNarrowedApiKey}")
        .WithEnvironment(
            "AdminEndpoint__Authentication__1__Permissions__0",
            OrchestrationContract.AdminNarrowedPermission)
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
            OrchestrationContract.McpRateLimitMaxConcurrentRequests.ToString(CultureInfo.InvariantCulture))
        // The two bounds an authored send meets that nothing below the transport can prove: one organization this
        // deployment may never write to, and a ceiling on the people one caller may reach in a period. Both are stated
        // here rather than defaulted, because a suite that configured neither would pass whether or not either was
        // wired into the request path at all.
        //
        // The denied entry names a subdomain of the reserved testing domain the rest of this topology composes under,
        // so nothing else the suite addresses falls beneath it. The ceiling is set well above what every other test on
        // this host spends together and is reached by naming more people in one message than it permits at all, so the
        // refusal does not depend on which test ran first.
        .WithEnvironment(
            "MailDelivery__RecipientPolicy__DeniedDomains__0",
            OrchestrationContract.ComposedHostRefusedRecipientDomain)
        .WithEnvironment(
            "MailDelivery__SendCeilings__MaxRecipientsPerCaller",
            OrchestrationContract.ComposedHostCallerRecipientCeiling.ToString(CultureInfo.InvariantCulture))
        // The outbox worker runs here like it does on any deployment, and the submission host it offers a message to
        // does not resolve — so every send this host queues is claimed, attempted once, and deferred. Stretching the
        // first retry past the length of a run is what makes that one attempt the whole of what happens to a record: a
        // send stands recorded and withdrawable afterwards instead of being claimed again while a test is reading it.
        .WithEnvironment("MailDelivery__RetryBaseDelay", OrchestrationContract.ComposedHostDeliveryRetryDelay);

    // The probe section is handed the port the endpoint above allocated, which is what puts the two surfaces on one
    // socket. It is a second reference to the same endpoint rather than a second endpoint, because two endpoints are
    // two allocations and nothing would make them the same number. Stated after the chain for the reason the mutual-TLS
    // host's HTTPS port is: an endpoint cannot be referenced until the resource declaring it exists.
    mailFathomHost.WithEnvironment(
        "HealthEndpoints__Port",
        mailFathomHost
            .GetEndpoint(OrchestrationContract.HostHttpEndpointName)
            .Property(EndpointProperty.TargetPort));
}
else
{
    // The four host sockets and the client's own, each stated by the section that owns it and each on a free port this
    // run found unless this checkout pinned one. Found here rather than left to the orchestrator, which allocates a
    // port for the proxy it would put in front of a resource and refuses an endpoint that declares none while asking
    // for no proxy — and no proxy is what keeps the socket a client reaches the socket Kestrel opened.
    //
    // All five are found in one call whether or not any is pinned, because that is what makes them different from each
    // other; a pinned value then replaces the one it was found for and the others stay where they were put. The last
    // one belongs to the client below, and is found here rather than beside it for that reason: a second call would
    // release these before choosing, and two sockets handed one number is a run that fails on whichever binds second.
    var foundPorts = OrchestrationContract.FindFreePorts(5);
    var mcpEndpointPort = PinnedPort(OrchestrationContract.PinnedMcpEndpointPortKey) ?? foundPorts[0];
    var healthEndpointsPort = PinnedPort(OrchestrationContract.PinnedHealthEndpointsPortKey) ?? foundPorts[1];
    var adminEndpointPort = foundPorts[2];
    var clientEndpointPort = PinnedPort(OrchestrationContract.PinnedClientEndpointPortKey) ?? foundPorts[3];
    var clientPort = PinnedPort(OrchestrationContract.PinnedClientPortKey) ?? foundPorts[4];

    var mailAccountHost = builder
        .AddParameter("mail-account-host")
        .WithDescription("The IMAP server host name for the mailbox MailFathom synchronizes.");
    var mailAccountUserName = builder
        .AddParameter("mail-account-username")
        .WithDescription("The IMAP username for the mailbox MailFathom synchronizes.");
    var mailAccountPassword = builder
        .AddParameter("mail-account-password", secret: true)
        .WithDescription("The IMAP password or app password for the mailbox MailFathom synchronizes.");

    foreach (var setting in OrchestrationContract.DevelopmentHostEnvironment)
    {
        mailFathomHost.WithEnvironment(setting.Key, setting.Value);
    }

    mailFathomHost
        .WithEnvironment("MailSynchronization__Accounts__0__Host", mailAccountHost)
        .WithEnvironment("MailSynchronization__Accounts__0__UserName", mailAccountUserName)
        .WithEnvironment(
            "MailSynchronization__Accounts__0__Secrets__Password__SecretReference",
            ReferenceExpression.Create($"plaintext:{mailAccountPassword}"))
        // The MCP endpoint's own socket, stated to the app model and injected into the host's own configuration key, so
        // the number is written once rather than declared here and configured again beside it. Its scheme is tcp rather
        // than http, for the reason the probe endpoint's is: Aspire builds ASPNETCORE_URLS from the http and https
        // endpoints, and MailFathom refuses that variable outright — every surface states where it is served in its own
        // section. A tcp endpoint is recorded and published without reaching it; what a client connects with is still
        // HTTP.
        //
        // Bound by the host itself rather than by a proxy in front of it: the socket a client connects to is then the
        // socket Kestrel opened, which is what keeps a TLS handshake and a client certificate a conversation with the
        // host.
        //
        // Its number is a free one this run found, unless this checkout stated the number it wants. An MCP client's
        // configuration names an address once, which is what a fixed port was here for, but a fixed port is one port
        // and several checkouts of this repository run their orchestrations at the same time — every run after the
        // first then failed to bind and exited. So the stable address is what a developer asks for rather than what
        // every run imposes on every other: 8080 and 8081 are the numbers the container image publishes, and pinning
        // them is what makes a local run and a deployed one answer on the same ports.
        //
        // No HTTPS endpoint accompanies this one. Kestrel serves an https:// address it was handed with no endpoint
        // configuration out of the ASP.NET Core development certificate, and MailFathom never serves a listener out of
        // one — a developer who wants TLS locally configures McpEndpoint:Https the way a deployment does, which is also
        // the shape they will ship.
        .WithEndpoint(
            name: OrchestrationContract.HostHttpEndpointName,
            scheme: "tcp",
            port: mcpEndpointPort,
            targetPort: mcpEndpointPort,
            isProxied: false,
            env: "McpEndpoint__Port")
        // The probe listener, on a socket of its own here rather than beside the MCP endpoint the way the integration
        // topology serves it, and declared beside it so that both ports are read in one place and the orchestrator
        // shows the one a developer curls.
        //
        // On loopback, which is what a developer machine wants and a container does not: the probes answer without a
        // credential, and nothing on a local network has any business asking them. That is also why this socket is
        // separate — a shared one is one socket, so the probes would answer wherever the MCP endpoint does.
        //
        // WithHttpHealthCheck is unavailable for the reason the scheme above is tcp: it derives its address from an
        // http endpoint, and this app model declares none.
        .WithEnvironment("HealthEndpoints__BindAddress", "127.0.0.1")
        .WithEndpoint(
            name: "health",
            scheme: "tcp",
            port: healthEndpointsPort,
            targetPort: healthEndpointsPort,
            isProxied: false,
            env: "HealthEndpoints__Port")
        .WithEndpoint(
            name: OrchestrationContract.HostAdminEndpointName,
            scheme: "tcp",
            port: adminEndpointPort,
            targetPort: adminEndpointPort,
            isProxied: false,
            env: "AdminEndpoint__Port")
        // The client surface's socket, declared exactly as the MCP endpoint's is. The normal app model enables it and
        // admits the password method so a client has a usable service on its first run; the credential itself is
        // provisioned through the administrative API after the host reports startup readiness. The client resource
        // below is served its address, so a developer reaches this surface without typing a port anywhere.
        //
        // A socket of its own rather than the MCP endpoint's, though every surface's default port would have shared
        // one. What cannot be shared is the bind address: a wildcard beside a specific address on one port is two
        // sockets the operating system grants only one of, so sharing would either publish the client surface wherever
        // the MCP endpoint is published or move that endpoint to loopback as a side effect. On loopback because the
        // only thing that calls this is a client running on this machine.
        .WithEnvironment("ClientEndpoint__BindAddress", OrchestrationContract.DeveloperLoopbackAddress)
        .WithEndpoint(
            name: OrchestrationContract.HostClientEndpointName,
            scheme: "tcp",
            port: clientEndpointPort,
            targetPort: clientEndpointPort,
            isProxied: false,
            env: "ClientEndpoint__Port");

    builder.Services
        .AddHttpClient(DevelopmentCredentialProvisioningWorker.HttpClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(10))
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
    builder.Services.AddHostedService(provider => new DevelopmentCredentialProvisioningWorker(
        provider.GetRequiredService<ResourceNotificationService>(),
        provider.GetRequiredService<IHttpClientFactory>(),
        mailFathomHost.GetEndpoint("health"),
        mailFathomHost.GetEndpoint(OrchestrationContract.HostAdminEndpointName),
        TimeProvider.System,
        provider.GetRequiredService<ILogger<DevelopmentCredentialProvisioningWorker>>()));

    // The client, in the one topology it belongs to. What the app model reaches into the other stack with is a
    // directory and a command: no package under frontend/ enters backend/MailFathom.slnx, no backend project references
    // one, and MSBuild is never told the two are related — so a build of the service still restores nothing the client
    // needs. The boundary the repository keeps is a compile-time one, and this is a process the orchestration starts
    // beside another process.
    //
    // An executable rather than a project resource, because there is no project: the client is a Vite development
    // server, and what starts it is the workspace's own `dev` script run from frontend/ by its package manager. The
    // script forwards these arguments through the workspace filter to Vite unchanged, so the socket stated here is the
    // socket the server binds rather than one it chose.
    //
    // It waits for nothing. A development server serves the page whether or not the service behind it has started, and
    // waiting would trade a working page for a slower one; the client's own first request is what discovers the state
    // of the service.
    if (OrchestrationContract.ResolveClientEnabled(builder.Configuration[OrchestrationContract.ClientEnabledKey]))
    {
        builder
            .AddExecutable(
                OrchestrationContract.ClientResourceName,
                OrchestrationContract.ClientPackageManagerCommand,
                Path.Combine(builder.AppHostDirectory, OrchestrationContract.ClientWorkspaceDirectory),
                OrchestrationContract.ClientDevelopmentServerScript,
                OrchestrationContract.ClientDevelopmentServerHostArgument,
                OrchestrationContract.DeveloperLoopbackAddress,
                OrchestrationContract.ClientDevelopmentServerPortArgument,
                clientPort.ToString(CultureInfo.InvariantCulture),
                OrchestrationContract.ClientDevelopmentServerStrictPortArgument)
            // Where the service is, handed to the development server's process rather than to a build. Vite exposes
            // its own VITE_-prefixed environment on `import.meta.env`, so the page the server serves reads the port
            // this run took without a property, a generated file, or a rebuild — which is what the React stack made
            // possible and the WebAssembly one did not.
            //
            // CORS is not part of the join. The local topology leaves ClientEndpoint:Cors:AllowedOrigins unstated,
            // which is the product default of every browser origin, so a page served under either loopback spelling is
            // answered — naming one of them here is what made a tab opened as `localhost` look like an empty mailbox
            // while the same tab opened as `127.0.0.1` worked.
            .WithEnvironment(
                OrchestrationContract.ClientServiceAddressVariable,
                OrchestrationContract.ResolveDevelopmentServiceAddress(clientEndpointPort))
            // Unproxied for the reason the host's sockets are: what the app model publishes is then what a browser
            // connects to. On loopback, because a development build served against a developer's own machine is not
            // something anything on a local network has any business loading.
            .WithHttpEndpoint(
                name: OrchestrationContract.ClientHttpEndpointName,
                port: clientPort,
                targetPort: clientPort,
                isProxied: false)
            // Aspire's default endpoint host is `localhost`, which resolves to the IPv6 loopback before the IPv4 one on
            // an ordinary machine — and the development server binds only the address stated above. Left at the
            // default, the dashboard would link a socket nothing answers on while the page was alive beside it.
            .WithEndpoint(
                OrchestrationContract.ClientHttpEndpointName,
                endpoint => endpoint.TargetHost = OrchestrationContract.DeveloperLoopbackAddress,
                createIfNotExists: false);
    }
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
    // path derived from the namespace instead of to the output directory, which buries it under backend/src/Infrastructure/MailFathom.
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
        // Deliberately unauthenticated — no authentication entry is configured at all — because what this host exists
        // to prove is which certificate the endpoint judges, and a credential in front of that would make every refusal
        // answerable by two controls instead of one.
        // Stated rather than inferred from the profiles below. Terminating TLS is the transport's answer now, and a
        // profile configured under the clear-text default is refused at startup precisely so that a deployment cannot
        // believe it enabled TLS while nothing served it.
        .WithEnvironment("McpEndpoint__Transport", "HttpsOnly")
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
