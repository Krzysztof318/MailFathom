// Copyright © 2026 Krzysztof Kasprowicz

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

    /// <summary>The EF Core migration tool resource.</summary>
    public const string MigrationsResourceName = "mailmcp-migrations";

    /// <summary>The app host argument that selects the integration-test topology, written as <c>IntegrationTesting=true</c>.</summary>
    /// <remarks>
    /// A configuration key rather than an environment variable, because the testing builder passes app host arguments
    /// straight into the same command-line configuration provider a developer would use, and a key that only exists
    /// while a test drives the model cannot be set by accident on a machine that runs <c>aspire run</c>.
    /// </remarks>
    public const string IntegrationTestingConfigurationKey = "IntegrationTesting";

    /// <summary>The prefix every container and volume the integration-test topology creates is named with.</summary>
    /// <remarks>
    /// Test containers and volumes are ephemeral, and a run that is killed rather than shut down leaves both behind.
    /// The shared prefix is what makes the leftovers identifiable without inspecting them, so removing them is one
    /// filtered command rather than a decision per resource; <c>scripts/run-integration-tests.sh</c> makes exactly that
    /// removal part of every run.
    /// </remarks>
    public const string EphemeralResourceNamePrefix = "mailmcp-integrationtests";
}
