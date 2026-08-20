// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace MailFathom.AppHost;

/// <summary>The resource names and switches this app model publishes to anything that drives it.</summary>
/// <remarks>
/// The integration-test suite starts this same app model rather than defining a second container topology, so it has to
/// name the resources it waits on and the switch that selects the ephemeral shape. Those names are declared here rather
/// than repeated as literals on both sides, because a rename that reached only one side would leave the suite waiting
/// for a resource the orchestration no longer has and reporting it as a timeout.
/// </remarks>
public static class OrchestrationContract
{
    /// <summary>The PostgreSQL server resource.</summary>
    public const string PostgresResourceName = "postgres";

    /// <summary>The login the orchestrated PostgreSQL server is created with.</summary>
    /// <remarks>
    /// The conventional one, so a <c>psql</c> invocation or a database tool needs nothing beyond the host and the port.
    /// </remarks>
    public const string PostgresUserName = "postgres";

    /// <summary>The password that login is created with.</summary>
    /// <remarks>
    /// <para>
    /// A literal under the same restriction as <see cref="MailServerAccountPassword" />. It authenticates one
    /// development database, on a container whose port Aspire publishes on the loopback address alone, and it unlocks
    /// nothing a deployment runs: a deployed MailFathom reaches PostgreSQL through a connection string whose secret is
    /// provisioned, and this app model builds none of it.
    /// </para>
    /// <para>
    /// Stated rather than generated, which is what removes a failure mode instead of documenting one. Aspire generates
    /// a password per run and can only keep it stable by persisting it, while PostgreSQL applies a password once, when
    /// it initializes an empty data directory — so a generated password and a data volume that outlives it can diverge,
    /// and the server then reports <c>password authentication failed</c> on a database nothing was wrong with. A value that
    /// never changes cannot diverge from one.
    /// </para>
    /// </remarks>
    public const string PostgresPassword = "postgres";

    /// <summary>The database the connection string is issued for.</summary>
    public const string DatabaseResourceName = "mailfathom";

    /// <summary>The identifier the development key ring seals local values under.</summary>
    /// <remarks>The identifier is written into every sealed row, so it names the ring rather than a date: a local database is re-sealed by being reset, never by a rotation.</remarks>
    public const string DataEncryptionKeyId = "development";

    /// <summary>The operator-facing label of the development key, which a validation failure would name.</summary>
    public const string DataEncryptionKeyName = "mailfathom-development-data-key";

    /// <summary>The development data-encryption key, base64 of exactly 32 bytes.</summary>
    /// <remarks>
    /// <para>
    /// Stated rather than generated, for the reason <see cref="PostgresPassword" /> is and with more at stake. A
    /// generated key is kept stable only by persisting it, and the data volume it protects outlives whatever store that
    /// would be — a diverged password reports an authentication error, while a diverged key leaves every locally sealed
    /// row unopenable with nothing to report but a failed authentication tag. A value that never changes cannot diverge
    /// from one, and resetting the local database is what re-seals it.
    /// </para>
    /// <para>
    /// It decodes to the ASCII text <c>mailfathom-development-only-key!</c>, so anyone who meets these bytes in a local
    /// database or a log finds out what they are by decoding them. It protects one developer's synthetic mail on a
    /// container published on the loopback address alone and unlocks nothing a deployment holds: a deployed MailFathom
    /// resolves its key from a secret reference an operator provisioned, which
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0005-data-encryption-key-ring-and-provisioning.md">ADR 0005</see> records and
    /// which this app model builds no part of.
    /// </para>
    /// </remarks>
    public const string DataEncryptionKeyMaterial = "bWFpbGZhdGhvbS1kZXZlbG9wbWVudC1vbmx5LWtleSE=";

    /// <summary>The MailFathom host project resource.</summary>
    public const string HostResourceName = "mailfathom-host";

    /// <summary>The endpoint the MailFathom host serves its MCP surface on.</summary>
    /// <remarks>Declared with the <c>tcp</c> scheme and its port injected into <c>McpEndpoint:Port</c>, for the reason <see cref="HostAdminEndpointName" /> gives: the host refuses <c>ASPNETCORE_URLS</c>, which Aspire would build from an http endpoint. The name is kept because it is what the orchestration and the suite resolve the address by; what a client speaks to it is HTTP.</remarks>
    public const string HostHttpEndpointName = "http";

    /// <summary>The IP address the socket named by <see cref="HostHttpEndpointName" /> binds under the integration-test topology.</summary>
    /// <remarks>
    /// One value read by two configuration sections, because that socket serves two surfaces: the MCP endpoint and the
    /// probes. A socket is identified by its address and its port, so a section stating one address while the other
    /// defaults would describe two sockets rather than one, which the host refuses at startup — a refusal this suite
    /// would meet as a host that never started rather than as the configuration mistake it is.
    /// </remarks>
    public const string ComposedHostBindAddress = "0.0.0.0";

    /// <summary>The second MailFathom host project resource, the one served over HTTPS behind mutual TLS.</summary>
    /// <remarks>
    /// <para>
    /// Present only under <see cref="IntegrationTestingArgument" />, and a second resource rather than a posture applied
    /// to <see cref="HostResourceName" />. Whether a client certificate is required is one answer for a whole process,
    /// so a host that refuses a request presenting none cannot also be the host every other composed test reaches
    /// without one. Keeping them apart is what lets those tests stay about the credential, the origin, and the limiter
    /// they were written for.
    /// </para>
    /// <para>
    /// It is started explicitly, like <see cref="HostResourceName" /> and for the same reason: it opens the database the
    /// rest of the suite writes to.
    /// </para>
    /// </remarks>
    public const string MutualTlsHostResourceName = "mailfathom-mtls-host";

    /// <summary>The HTTPS endpoint that host serves its surface on, including the MCP endpoint.</summary>
    public const string MutualTlsHostHttpsEndpointName = "https";

    /// <summary>The DNS domain the mutual-TLS host's HTTPS profile publishes and selects on.</summary>
    /// <remarks>
    /// The loopback name, because that is the name the suite's client asks for during the handshake and the only one an
    /// orchestrated endpoint resolves to. A profile publishes an exact name and refuses every other, so the certificate
    /// the suite issues has to carry this one as a subject alternative name.
    /// </remarks>
    public const string MutualTlsHostDomain = "localhost";

    /// <summary>The IP address the mutual-TLS host's HTTPS listener binds.</summary>
    /// <remarks>Both loopback families, because whether a client resolves <see cref="MutualTlsHostDomain" /> to IPv4 or IPv6 first is the machine's choice rather than the suite's.</remarks>
    public const string MutualTlsHostBindAddress = "::";

    /// <summary>The name of the one client-certificate trust profile that host configures.</summary>
    public const string MutualTlsClientProfileName = "integration-tests-client";

    /// <summary>The DNS name a certificate must carry as a subject alternative name for that profile to accept it.</summary>
    /// <remarks>The domain is reserved for testing, like every other name this topology invents.</remarks>
    public const string MutualTlsClientDnsName = "client.mailfathom.test";

    /// <summary>The environment variable the mutual-TLS host reads its server certificate chain from.</summary>
    /// <remarks>
    /// The material itself is deliberately absent from this app model and from the repository: the suite generates a
    /// certificate authority, a server identity, and the client certificates per run and injects them into these
    /// variables before the application is built. The app model states only where the host looks, which is what keeps a
    /// private key out of source control while both sides still read one name.
    /// </remarks>
    public const string MutualTlsServerCertificateChainVariable = "MAILFATHOM_INTEGRATIONTESTS_SERVER_CERTIFICATE_CHAIN";

    /// <summary>The environment variable the mutual-TLS host reads its server private key from.</summary>
    public const string MutualTlsServerPrivateKeyVariable = "MAILFATHOM_INTEGRATIONTESTS_SERVER_PRIVATE_KEY";

    /// <summary>The environment variable the mutual-TLS host reads its one client-certificate trust anchor from.</summary>
    public const string MutualTlsClientTrustAnchorVariable = "MAILFATHOM_INTEGRATIONTESTS_CLIENT_TRUST_ANCHOR";

    /// <summary>The name the integration-test topology configures its one MCP API key under.</summary>
    /// <remarks>Present only under <see cref="IntegrationTestingArgument" />, like the key itself.</remarks>
    public const string McpApiKeyName = "integration-tests";

    /// <summary>The MCP API key the integration-test topology's host accepts.</summary>
    /// <remarks>
    /// A literal for the same reason <see cref="MailServerAccountPassword" /> is one, and under the same restriction.
    /// It authenticates against one host that exists for the duration of one test run, is reachable only from that run,
    /// and is destroyed with it; there is no deployment it could also unlock. Declaring it here is what keeps the app
    /// model that configures the endpoint and the suite that calls it reading one value, so a change cannot reach only
    /// one side and surface as an authentication failure that says nothing about the behavior under test. Nothing
    /// outside the ephemeral topology may use it.
    /// </remarks>
    public const string McpApiKey = "integration-tests-only-mcp-api-key";

    /// <summary>The name the integration-test topology configures a second, deliberately expendable MCP API key under.</summary>
    /// <remarks>
    /// A rate limit is counted per client, so a test that exhausts one has to exhaust a client nothing else in the suite
    /// depends on. This key exists to be spent: the burst that proves the limiter is wired takes it to zero, and
    /// <see cref="McpApiKeyName" /> keeps its own capacity untouched, which is also what makes the partitions'
    /// independence observable from outside the process.
    /// </remarks>
    public const string McpExpendableApiKeyName = "integration-tests-expendable";

    /// <summary>The second MCP API key the integration-test topology's host accepts.</summary>
    /// <remarks>A literal under the same restriction as <see cref="McpApiKey" />, and it authenticates the same ephemeral host.</remarks>
    public const string McpExpendableApiKey = "integration-tests-only-mcp-expendable-key";

    /// <summary>The burst one MCP client may spend in the integration-test topology before it is refused.</summary>
    /// <remarks>
    /// Declared here because the suite has to send more than this to observe a refusal, and a value that reached only
    /// the app model would leave the burst either too small to refuse anything or needlessly large. It is deliberately
    /// well above what any other test in the suite spends, and only <see cref="McpExpendableApiKeyName" /> ever spends
    /// it, so exhausting it disturbs nothing that runs afterwards.
    /// </remarks>
    public const int McpRateLimitTokenCapacity = 20;

    /// <summary>How often the integration-test topology restores a client's spent MCP capacity.</summary>
    /// <remarks>
    /// <para>
    /// Long enough that no capacity comes back while a run is in progress, which is what makes a refusal a property of
    /// how much the burst spent rather than of how fast the machine dispatched it. A period of a second, which this
    /// deliberately is not, makes the outcome depend on arrival pacing: the framework restores the whole capacity every
    /// period, so a burst that trickles in below that rate is served in full and refuses nothing. Measured on a cold
    /// host, sixty requests dispatched together arrived over thirteen seconds — under five a second against twenty a
    /// second being restored — and the test that exists to observe a refusal observed none.
    /// </para>
    /// <para>
    /// Nothing is lost by the spent client staying spent. Rate limits are counted per client, and
    /// <see cref="McpExpendableApiKeyName" /> exists to be taken to zero exactly once; every other test authenticates
    /// with <see cref="McpApiKeyName" />, whose own capacity the burst never touches.
    /// </para>
    /// </remarks>
    public const string McpRateLimitReplenishmentPeriod = "00:10:00";

    /// <summary>How many MCP requests the integration-test topology's host serves at once, across every client.</summary>
    /// <remarks>
    /// Raised far above the product default, and above any burst the suite sends, so that the process-wide concurrency
    /// limit cannot be what refuses a request. Left at the default it would sit at the same order as
    /// <see cref="McpRateLimitTokenCapacity" />, and a burst large enough to exhaust a client's tokens would also exceed
    /// the permits — leaving a test unable to say which of the two limiters answered, and passing even if the per-client
    /// policy were never attached to the route.
    /// </remarks>
    public const int McpRateLimitMaxConcurrentRequests = 200;

    /// <summary>The one browser origin the integration-test topology's MCP endpoint serves.</summary>
    /// <remarks>
    /// The topology narrows the origins deliberately rather than leaving the permissive default, because a suite that
    /// only ever saw allow-any could not tell an origin check that works from one that is not wired in at all. The
    /// domain is reserved for testing.
    /// </remarks>
    public const string McpPermittedOrigin = "https://client.mailfathom.test";

    /// <summary>The endpoint the MailFathom host serves its administrative surface on.</summary>
    /// <remarks>
    /// <para>
    /// Present only under <see cref="IntegrationTestingArgument" />. A developer's orchestration administers nothing
    /// over the network, and an endpoint enabled for them would be a socket nobody asked for.
    /// </para>
    /// <para>
    /// Declared with the <c>tcp</c> scheme rather than <c>http</c>, for the reason every endpoint on this resource is:
    /// Aspire builds <c>ASPNETCORE_URLS</c> from the http and https endpoints, and MailFathom refuses that variable
    /// outright, because each surface states where it is served in its own configuration section. A tcp endpoint is
    /// published without reaching that variable, and its port is injected into the section that owns it; what the suite
    /// connects to it with is still HTTP.
    /// </para>
    /// </remarks>
    public const string HostAdminEndpointName = "admin";

    /// <summary>The IP address the administrative listener binds under the integration-test topology.</summary>
    /// <remarks>Both loopback families, for the reason <see cref="MutualTlsHostBindAddress" /> is what it is: whether the orchestration reaches this listener over IPv4 or IPv6 is the machine's choice rather than the suite's.</remarks>
    public const string AdminEndpointBindAddress = "::";

    /// <summary>The name the integration-test topology configures its one administrative API key under.</summary>
    /// <remarks>Present only under <see cref="IntegrationTestingArgument" />, like the key itself. It is also what the endpoint reports back as the credential that authenticated, so the suite asserts against this name rather than against the material.</remarks>
    public const string AdminApiKeyName = "integration-tests-admin";

    /// <summary>The administrative API key the integration-test topology's host accepts.</summary>
    /// <remarks>
    /// A literal under the same restriction as <see cref="McpApiKey" />, and it authenticates the same ephemeral host.
    /// It is deliberately a different value from every MCP key: reading a mailbox and administering the service that
    /// reads it are different authorities, and a suite whose two surfaces shared a key could not observe that neither
    /// one's credential authenticates the other.
    /// </remarks>
    public const string AdminApiKey = "integration-tests-only-admin-api-key";

    /// <summary>The name of a second administrative key, whose entry grants one permission rather than the surface.</summary>
    /// <remarks>The key above writes no grant and therefore reaches everything administrative, so a suite holding only that one could never observe a route refusing a caller over what it holds.</remarks>
    public const string AdminNarrowedApiKeyName = "integration-tests-admin-read-only";

    /// <summary>A second administrative API key, admitted by an entry granting <see cref="AdminNarrowedPermission" /> and nothing else.</summary>
    /// <remarks>A literal under the same restriction as <see cref="AdminApiKey" />, and deliberately a different value: what it exists to make observable is that two credentials on one surface reach different routes.</remarks>
    public const string AdminNarrowedApiKey = "integration-tests-only-admin-read-only-api-key";

    /// <summary>The one permission the entry above grants, which is what its key holds and the whole of what it reaches.</summary>
    public const string AdminNarrowedPermission = "mailfathom.admin.read";

    /// <summary>The EF Core migration tool resource.</summary>
    public const string MigrationsResourceName = "mailfathom-migrations";

    /// <summary>The IMAP and SMTP server the integration-test topology synchronizes against.</summary>
    /// <remarks>
    /// Present only under <see cref="IntegrationTestingArgument" />. A developer's orchestration synchronizes the
    /// accounts that developer configured, and starting a mail server beside them would advertise a mailbox nothing
    /// points at.
    /// </remarks>
    public const string MailServerResourceName = "mailserver";

    /// <summary>The mail server endpoint the IMAP adapter connects to.</summary>
    public const string MailServerImapEndpointName = "imap";

    /// <summary>The mail server endpoint the suite seeds mail through.</summary>
    public const string MailServerSmtpEndpointName = "smtp";

    /// <summary>The mail server endpoint that answers whether the server is accepting mail yet.</summary>
    public const string MailServerApiEndpointName = "api";

    /// <summary>The IMAP and SMTP login of the one synthetic mailbox the integration-test topology serves.</summary>
    public const string MailServerAccountUserName = "mailfathom";

    /// <summary>The address mail is addressed to in order to reach <see cref="MailServerAccountUserName" />.</summary>
    /// <remarks>The domain is reserved for testing, so nothing addressed here can leave the container it is delivered in.</remarks>
    public const string MailServerAccountEmailAddress = "mailfathom@mailfathom.test";

    /// <summary>The password of that synthetic mailbox.</summary>
    /// <remarks>
    /// A literal rather than a generated or referenced secret, and deliberately so. It authenticates one throwaway
    /// mailbox on a container that exists for the duration of one test run, is reachable only from that run, and is
    /// destroyed with it; there is no account it could also unlock. Declaring it here is what keeps the app model that
    /// configures the server and the suite that logs into it reading one value, so a change cannot reach only one side
    /// and surface as an authentication failure. Nothing outside the ephemeral topology may use it.
    /// </remarks>
    public const string MailServerAccountPassword = "integration-tests-only";

    /// <summary>The personal-data analyzer the integration-test topology scans against.</summary>
    /// <remarks>
    /// <para>
    /// Present only under <see cref="IntegrationTestingArgument" />. A developer's orchestration deploys it only where the
    /// deployment they are running switched personal-data scanning on, which is a property of that deployment's
    /// configuration rather than of the app model, and starting one beside every local run would cost a container and its
    /// language model for a feature that is off by default.
    /// </para>
    /// <para>
    /// The suite starts it because it is the one part of the personal-data scanner no substitute settles: a hand-written
    /// payload proves the mapping works on a hand-written payload, not that the image an operator pulls answers the request
    /// MailFathom builds with the entities MailFathom expects.
    /// </para>
    /// </remarks>
    public const string PersonalDataAnalyzerResourceName = "presidio-analyzer";

    /// <summary>The analyzer's own HTTP port, which its image publishes and its entrypoint binds.</summary>
    public const int PersonalDataAnalyzerContainerPort = 3000;

    /// <summary>The endpoint the analyzer answers analysis and supported-entity requests on.</summary>
    public const string PersonalDataAnalyzerEndpointName = "http";

    /// <summary>The spam daemon the integration-test topology scores against.</summary>
    /// <remarks>
    /// <para>
    /// Present only under <see cref="IntegrationTestingArgument" />, for the reason the analyzer is: a developer's
    /// orchestration deploys one only where the deployment they are running switched the scanner on, and starting one
    /// beside every local run would cost a container and its rule corpus for a feature that is off by default.
    /// </para>
    /// <para>
    /// The suite starts it because it is the one part of the spam scanner no substitute settles: a scripted daemon
    /// proves the parser handles the payload somebody hand-wrote, not that the image an operator pulls answers the
    /// request MailFathom builds in the shape MailFathom parses.
    /// </para>
    /// </remarks>
    public const string SpamScannerResourceName = "spamassassin";

    /// <summary>The daemon's own port, which its image publishes and its entrypoint binds.</summary>
    public const int SpamScannerContainerPort = 783;

    /// <summary>The endpoint the daemon answers its line protocol on.</summary>
    /// <remarks>Declared with the <c>tcp</c> scheme because that is what it is: the daemon speaks neither HTTP nor anything Aspire can probe with a route.</remarks>
    public const string SpamScannerEndpointName = "spamd";

    /// <summary>The MailFathom account identifier every occurrence the integration-test topology stores belongs to.</summary>
    /// <remarks>
    /// Declared here because two sides have to agree on it: the suite writes its mail under this identifier, and the
    /// composed host is configured to serve it so that a tool call over the MCP endpoint reads the mail the suite
    /// stored. A deployment serves the accounts configuration names, so a host that named none would answer every
    /// mailbox read with an empty window over a database that was not empty — which reads as the query being wrong
    /// rather than as the account being absent.
    /// </remarks>
    public const string ServedMailAccountId = "integration";

    /// <summary>The display name the integration-test topology publishes that account under.</summary>
    /// <remarks>
    /// Declared beside the identifier and deliberately different from it, because a display name is required
    /// configuration and the two spellings are separately resolvable: a topology that reused the identifier here would
    /// let a contract test pass while only one of the two ways of naming an account worked.
    /// </remarks>
    public const string ServedMailAccountDisplayName = "Integration mailbox";

    /// <summary>The one folder the composed host maps, which is the folder a tool call over its MCP endpoint reads.</summary>
    /// <remarks>
    /// Declared here for the reason the account identifier is, and it is the same failure one step further in: a mapping
    /// is what makes MailFathom have a folder at all, so a host that mapped none resolves an empty readable scope and
    /// answers every tool call with an empty window over mail the suite had just stored. One alias rather than the
    /// suite's whole list, because the composed host is reached by the tests that prove a request pipeline and only one
    /// of them reads mail; a test that needs a second folder there names it here beside this one.
    /// </remarks>
    public const string ComposedHostReadableFolderAlias = "mcp-tool-contract";

    /// <summary>The address the composed host's one account sends as, which is also the login its delivery block uses.</summary>
    /// <remarks>
    /// Declared here because the account it belongs to is: a tool that queues a send is refused outright unless the
    /// account it sends as configures an address to send from, so a topology that named none would answer every reply
    /// and every forward with a deployment that cannot send. The domain is the reserved testing one, so nothing
    /// composed under it could leave the run it was composed in even if something transmitted — and nothing does,
    /// because a tool call writes a record and the composed host runs no delivery pass over it.
    /// </remarks>
    public const string ComposedHostSendingAddress = "mailfathom@mailfathom.test";

    /// <summary>The submission host the composed host's one account names, which nothing under this topology connects to.</summary>
    /// <remarks>
    /// A submission endpoint is configured because its presence is what makes the account able to send at all, and its
    /// address is a name rather than the orchestrated mail server's: the composed host queues sends and never delivers
    /// them — synchronization and the outbox pass are both off under this topology — so a reachable host would be
    /// configuration nothing acts on. The domain is the reserved testing one for the same reason the address above is.
    /// </remarks>
    public const string ComposedHostSubmissionHost = "smtp.mailfathom.test";

    /// <summary>The one organization the composed host's recipient policy refuses, whoever a caller says asked for it.</summary>
    /// <remarks>
    /// A subdomain of the reserved testing domain the rest of this topology composes under, which is what keeps the
    /// entry from reaching anything else the suite addresses: a denied domain reaches the names beneath it, and denying
    /// the parent would refuse the account's own sending address. Declared here because a policy that refuses nobody
    /// would let a suite pass while the judgement was never reached on the surface a caller uses.
    /// </remarks>
    public const string ComposedHostRefusedRecipientDomain = "refused.mailfathom.test";

    /// <summary>The people one caller of the composed host may write to in a period, which one message can exceed.</summary>
    /// <remarks>
    /// Counted per calling principal over an epoch-anchored window, so what any test spends stays spent for the rest of
    /// the run. The number is well above what every other test on this host addresses together, and the refusal is
    /// reached by naming more people in one message than the ceiling permits at all rather than by exhausting it, so
    /// neither the order the collection ran in nor a test added later decides the outcome.
    /// </remarks>
    public const int ComposedHostCallerRecipientCeiling = 4;

    /// <summary>The whole app host argument that selects the integration-test topology.</summary>
    /// <remarks>
    /// Matched against the argument list itself rather than read through <c>IDistributedApplicationBuilder.Configuration</c>,
    /// which also binds environment variables: an <c>IntegrationTesting</c> variable set on a developer or automation
    /// machine would otherwise put an ordinary <c>aspire run</c> on the fixed-name ephemeral database and leave its
    /// volume under the prefix <c>scripts/run-integration-tests.sh</c> deletes. Selecting the topology has to be
    /// something only the caller starting the app model can do, and an argument is the only input ambient state cannot
    /// supply.
    /// </remarks>
    public const string IntegrationTestingArgument = "IntegrationTesting=true";

    /// <summary>The prefix every container and volume the integration-test topology creates is named with.</summary>
    /// <remarks>
    /// Test containers and volumes are ephemeral, and a run that is killed rather than shut down leaves both behind.
    /// The shared prefix is what makes the leftovers identifiable without inspecting them, so removing them is one
    /// filtered command rather than a decision per resource. It is the leading part of a name rather than the whole of
    /// one: <see cref="ResolveEphemeralResourceNamePrefix" /> appends this run's own identifier after it.
    /// </remarks>
    public const string EphemeralResourceNamePrefix = "mailfathom-integrationtests";

    /// <summary>The environment variable OpenSSL reads the path of its configuration file from.</summary>
    /// <remarks>
    /// <para>
    /// A variable this app model passes through rather than one it publishes a value for. It exists here because a
    /// developer whose mailbox is served by a mail server the platform's own TLS policy refuses sets it before starting
    /// the orchestration, and a resource Aspire starts inherits nothing of the kind on its own.
    /// </para>
    /// <para>
    /// The distinction is deliberate: deciding that a weaker TLS policy applies is an operator's act, taken once, in
    /// the environment they start MailFathom from. An app model that named a file of its own would take that decision
    /// for every developer who ever runs it.
    /// </para>
    /// </remarks>
    public const string OpenSslConfigurationVariable = "OPENSSL_CONF";

    /// <summary>The environment variable a caller states this run's ephemeral resource identifier in.</summary>
    /// <remarks>
    /// <para>
    /// Set by <c>scripts/run-integration-tests.sh</c>, which needs the identifier before the suite starts so that the
    /// removal it performs afterwards can name what this run created rather than everything the shared prefix matches.
    /// A sweep of the shared prefix would take a concurrent run's containers with it, which is the collision the
    /// identifier exists to prevent.
    /// </para>
    /// <para>
    /// An environment variable rather than an argument, unlike <see cref="IntegrationTestingArgument" />, and the
    /// difference is what each one decides. The argument selects a topology, so an ambient value could divert an
    /// ordinary run onto the ephemeral one; this names resources within a topology already selected, so an ambient
    /// value can only produce a differently named container. Left unset, the run generates its own.
    /// </para>
    /// </remarks>
    public const string EphemeralRunIdentifierVariable = "MAILFATHOM_INTEGRATIONTESTS_RUN_ID";

    /// <summary>The configuration key a developer states the MCP endpoint's port under to pin it.</summary>
    /// <remarks>
    /// <para>
    /// The ordinary topology takes a free port for each socket it publishes, because a fixed port is one port and several checkouts
    /// of this repository run their orchestrations at the same time: the second to start cannot bind what the first is
    /// holding, and it exits rather than falling back. What a fixed port buys is an address written once into an MCP
    /// client's configuration and never revisited, so it stays available as something a developer asks for — state the
    /// number here and the run binds it.
    /// </para>
    /// <para>
    /// Read from the app host's own configuration, which is what makes user secrets its natural home: pinning a port is
    /// a decision about one machine and belongs in nobody's checkout. That store is keyed by the app host's fixed
    /// <c>UserSecretsId</c>, so a value put in it holds for every checkout on the machine rather than for the one it was
    /// set from; its environment form, <c>Ports__McpEndpoint</c>, is what pins a port for a single run.
    /// </para>
    /// </remarks>
    public const string PinnedMcpEndpointPortKey = "Ports:McpEndpoint";

    /// <summary>The configuration key a developer states the probe listener's port under to pin it.</summary>
    /// <remarks>Read the way <see cref="PinnedMcpEndpointPortKey" /> is, and read on its own, so pinning one socket leaves the other free to move.</remarks>
    public const string PinnedHealthEndpointsPortKey = "Ports:HealthEndpoints";

    /// <summary>The configuration key a developer states the PostgreSQL server's host port under to pin it.</summary>
    /// <remarks>Read the way <see cref="PinnedMcpEndpointPortKey" /> is. Pinning it is what a database tool configured once wants; leaving it unset is what lets a second checkout start a server of its own.</remarks>
    public const string PinnedPostgresPortKey = "Ports:Postgres";

    /// <summary>The lowest number a pinned port may state.</summary>
    private const int MinimumPortNumber = 1;

    /// <summary>The highest number a pinned port may state.</summary>
    private const int MaximumPortNumber = 65535;

    /// <summary>How many characters a generated run identifier has.</summary>
    /// <remarks>Four bytes of randomness, which is short enough to read in a container listing and long enough that two runs starting in the same minute do not collide.</remarks>
    private const int GeneratedRunIdentifierLength = 8;

    /// <summary>How long a stated run identifier may be.</summary>
    /// <remarks>Container names are bounded and already carry the prefix, the run identifier, and a resource name; this keeps the caller's part of that from growing into the limit.</remarks>
    private const int MaximumRunIdentifierLength = 16;

    /// <summary>Builds the prefix this run names its ephemeral containers and volumes with.</summary>
    /// <param name="runIdentifier">The identifier the caller stated, or <see langword="null" /> when it stated none.</param>
    /// <returns><see cref="EphemeralResourceNamePrefix" /> followed by the run's identifier.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a stated identifier cannot appear in a container name.</exception>
    /// <remarks>
    /// A stated identifier is refused rather than replaced when it is unusable. Replacing it silently would leave the
    /// caller removing a prefix nothing was named with, which is the one outcome worse than not being given one at all:
    /// a run that leaks every container and volume it created while reporting that it cleaned up.
    /// </remarks>
    public static string ResolveEphemeralResourceNamePrefix(string? runIdentifier)
    {
        if (string.IsNullOrWhiteSpace(runIdentifier))
        {
            return $"{EphemeralResourceNamePrefix}-{RandomNumberGenerator.GetHexString(GeneratedRunIdentifierLength, lowercase: true)}";
        }

        var statedIdentifier = runIdentifier.Trim();

        if (!IsUsableInAContainerName(statedIdentifier))
        {
            throw new InvalidOperationException(
                $"{EphemeralRunIdentifierVariable} is '{statedIdentifier}', which cannot appear in a container name. State between 1 and {MaximumRunIdentifierLength} characters, each an ASCII lowercase letter or a digit, or leave it unset to have one generated.");
        }

        return $"{EphemeralResourceNamePrefix}-{statedIdentifier}";
    }

    /// <summary>Reads the port a developer pinned under <paramref name="configurationKey" />.</summary>
    /// <param name="configurationKey">The key the value was read from, which is what a refusal names.</param>
    /// <param name="statedPort">The value configuration holds under that key, or <see langword="null" /> when it holds none.</param>
    /// <returns>The stated port, or <see langword="null" /> when nothing states one, which is what leaves the caller to take a free one.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a stated value is not a TCP port number.</exception>
    /// <remarks>
    /// A stated value that is not a port is refused rather than ignored, for the reason an unusable run identifier is:
    /// falling back to a free port would answer a request for one fixed address with a different address every run, and
    /// nothing would say why.
    /// </remarks>
    public static int? ResolvePinnedPort(string configurationKey, string? statedPort)
    {
        if (string.IsNullOrWhiteSpace(statedPort))
        {
            return null;
        }

        var statedNumber = statedPort.Trim();

        if (!int.TryParse(statedNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < MinimumPortNumber or > MaximumPortNumber)
        {
            throw new InvalidOperationException(
                $"{configurationKey} is '{statedNumber}', which is not a TCP port number. State a number between {MinimumPortNumber} and {MaximumPortNumber}, or leave it unset to have a free one taken per run.");
        }

        return port;
    }

    /// <summary>Finds free TCP ports for the sockets the orchestration publishes without a proxy in front of them.</summary>
    /// <param name="count">How many ports the caller needs, which is how many sockets it declares.</param>
    /// <returns>That many ports, each free when it was asked for and none equal to another.</returns>
    /// <remarks>
    /// <para>
    /// An unproxied endpoint has to state its number: the orchestrator allocates a port for the proxy it would put in
    /// front of the resource, and there is no proxy here, so it refuses the endpoint rather than choosing for it. What
    /// keeps a run off another run's port is therefore this — the operating system's own answer to which port is free,
    /// which is the same answer it gives a proxy.
    /// </para>
    /// <para>
    /// Every port is asked for before any is released, which is what makes them different from one another. Asking one
    /// at a time would release each before the next was chosen, and two sockets handed the same number would leave the
    /// host refusing to open the second listener over a collision within one run.
    /// </para>
    /// <para>
    /// They are all released before the host binds them, so a process that asks in the same instant can take one first
    /// and the host then fails to start on an address already in use. That is a race a few milliseconds wide against a
    /// range of sixteen thousand ephemeral ports, and it fails loudly on the run that loses rather than silently on the
    /// one that wins; holding the sockets open until the host binds them is not available, because the host is a
    /// separate process.
    /// </para>
    /// </remarks>
    public static int[] FindFreePorts(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        List<TcpListener> probes = [];

        try
        {
            for (var index = 0; index < count; index++)
            {
                var probe = new TcpListener(IPAddress.Any, 0);

                probes.Add(probe);
                probe.Start();
            }

            return [.. probes.Select(static probe => ((IPEndPoint)probe.LocalEndpoint).Port)];
        }
        finally
        {
            foreach (var probe in probes)
            {
                probe.Dispose();
            }
        }
    }

    private static bool IsUsableInAContainerName(string identifier) =>
        identifier.Length <= MaximumRunIdentifierLength
        && identifier.All(static character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character));
}
