// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailMcp.AppHost;

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

    /// <summary>The database the connection string is issued for.</summary>
    public const string DatabaseResourceName = "mailmcp";

    /// <summary>The MailMcp host project resource.</summary>
    public const string HostResourceName = "mailmcp-host";

    /// <summary>The HTTP endpoint the MailMcp host serves its surface on, including the MCP endpoint.</summary>
    public const string HostHttpEndpointName = "http";

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
    /// well above what any other test in the suite spends, and it is restored every
    /// <see cref="McpRateLimitReplenishmentPeriod" />, so exhausting it disturbs nothing that runs afterwards.
    /// </remarks>
    public const int McpRateLimitTokenCapacity = 20;

    /// <summary>How often the integration-test topology restores a client's spent MCP capacity.</summary>
    public const string McpRateLimitReplenishmentPeriod = "00:00:01";

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
    public const string McpPermittedOrigin = "https://client.mailmcp.test";

    /// <summary>The EF Core migration tool resource.</summary>
    public const string MigrationsResourceName = "mailmcp-migrations";

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
    public const string MailServerAccountUserName = "mailmcp";

    /// <summary>The address mail is addressed to in order to reach <see cref="MailServerAccountUserName" />.</summary>
    /// <remarks>The domain is reserved for testing, so nothing addressed here can leave the container it is delivered in.</remarks>
    public const string MailServerAccountEmailAddress = "mailmcp@mailmcp.test";

    /// <summary>The password of that synthetic mailbox.</summary>
    /// <remarks>
    /// A literal rather than a generated or referenced secret, and deliberately so. It authenticates one throwaway
    /// mailbox on a container that exists for the duration of one test run, is reachable only from that run, and is
    /// destroyed with it; there is no account it could also unlock. Declaring it here is what keeps the app model that
    /// configures the server and the suite that logs into it reading one value, so a change cannot reach only one side
    /// and surface as an authentication failure. Nothing outside the ephemeral topology may use it.
    /// </remarks>
    public const string MailServerAccountPassword = "integration-tests-only";

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
    /// filtered command rather than a decision per resource; <c>scripts/run-integration-tests.sh</c> makes exactly that
    /// removal part of every run.
    /// </remarks>
    public const string EphemeralResourceNamePrefix = "mailmcp-integrationtests";
}
