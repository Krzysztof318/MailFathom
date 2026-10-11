// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Editing;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>
/// Covers the two commands that read and edit a settings policy. What these hold is the part of each act that lives in
/// the command rather than in the deployment: which scope an invocation addresses, the version a write is composed
/// over, and what a refusal tells an operator to do next.
/// </summary>
public sealed class PolicyCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    /// <summary>The code a deployment refuses a policy composed over a version another writer moved past with.</summary>
    private const int VersionSuperseded = 12008;

    /// <summary>The code a deployment refuses a candidate it judged invalid with, which stands here for every refusal that is not about the version.</summary>
    private const int CandidateInvalid = 12007;

    /// <summary>A policy stating one default for the users beneath it, which an editing session opens over.</summary>
    private const string OneDefault = """{"Users":{"Defaults":{"Language":"pl"}}}""";

    /// <summary>The same policy once it also holds a value against the users beneath it.</summary>
    private const string OneDefaultAndOneForcedValue =
        """{"Users":{"Defaults":{"Language":"pl"},"Forced":{"TimeZone":"Europe/Warsaw"}}}""";

    private static readonly Guid Organization = new("77777777-7777-7777-7777-777777777777");

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));

    /// <summary>An invocation naming no organization addresses the deployment's own policy, and says that is whose it printed.</summary>
    [Fact]
    public async Task Show_NoOrganizationNamed_ReadsTheDeploymentsPolicyAndSaysWhoseItIs()
    {
        // Arrange
        using var deployment = FakeSettingsPolicyDeployment.Holding(organization: null, OneDefault);

        // Act
        var exitCode = await this.RunAsync(deployment, "policy", "show", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.RequestsTo(HttpMethod.Get, AdminEndpointRoutes.SettingsPolicyPath(organizationId: null)));
        Assert.Equal(
            ["Settings policy of the deployment", "  version: 3", OneDefault],
            this.harness.Console.Lines);
    }

    /// <summary>A named organization is the scope the request is sent to, and the heading names it by the identifier every other command takes.</summary>
    [Fact]
    public async Task Show_AnOrganizationNamed_ReadsThatOrganizationsPolicyAndNamesIt()
    {
        // Arrange
        using var deployment = FakeSettingsPolicyDeployment.Holding(Organization, OneDefault);

        // Act
        var exitCode = await this.RunAsync(
            deployment, "policy", "show", "--organization", $"{Organization:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.RequestsTo(HttpMethod.Get, AdminEndpointRoutes.SettingsPolicyPath(Organization)));
        Assert.Equal(
            [$"Settings policy of organization {Organization:D}", "  version: 3", OneDefault],
            this.harness.Console.Lines);
    }

    /// <summary>A scope that stores no policy answers an empty one, and it is printed as it arrived: it is what the first edit is composed over.</summary>
    [Fact]
    public async Task Show_AScopeStoringNoPolicy_PrintsTheEmptyDocumentAtVersionZero()
    {
        // Arrange
        using var deployment = FakeSettingsPolicyDeployment.StoringNoPolicy();

        // Act
        var exitCode = await this.RunAsync(deployment, "policy", "show", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(["Settings policy of the deployment", "  version: 0", "{}"], this.harness.Console.Lines);
    }

    /// <summary>The printed policy matches the view an operator would edit it in.</summary>
    [Fact]
    public async Task Show_AYamlView_PrintsThePolicyAsYaml()
    {
        // Arrange
        using var deployment = FakeSettingsPolicyDeployment.Holding(organization: null, OneDefault);

        // Act
        var exitCode = await this.RunAsync(deployment, "policy", "show", "--format", "yaml", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("Language: pl", StringComparison.Ordinal));
    }

    /// <summary>A 404 on an organization's policy route means the organization rather than the port, so the operator is sent to the listing rather than after a listener.</summary>
    [Fact]
    public async Task Show_AnOrganizationTheDeploymentDoesNotHold_SaysSoAndNamesTheListing()
    {
        // Arrange
        using var deployment = FakeSettingsPolicyDeployment.Holding(organization: null, OneDefault);

        // Act
        var exitCode = await this.RunAsync(
            deployment, "policy", "show", "--organization", $"{Organization:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Failures,
            line => line.Contains("'mfctl organization list'", StringComparison.Ordinal));
        Assert.Empty(this.harness.Console.Lines);
    }

    /// <summary>What the operator saved is what is committed, over the version the buffer was opened at.</summary>
    [Fact]
    public async Task Edit_AnEditedPolicy_CommitsWhatWasSavedOverTheVersionItWasOpenedAt()
    {
        // Arrange
        using var deployment = FakeSettingsPolicyDeployment.Holding(organization: null, OneDefault);

        this.harness.EditsTheBufferInto(OneDefaultAndOneForcedValue);

        // Act
        var exitCode = await this.RunAsync(deployment, "policy", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var sent = Assert.Single(
            deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.SettingsPolicyPath(organizationId: null)));

        Assert.Equal(FakeSettingsPolicyDeployment.PolicyVersion, ReadVersion(sent.ContentAsUtf8String()));
        Assert.Equal(OneDefaultAndOneForcedValue, ReadDocument(sent.ContentAsUtf8String()));
        Assert.Contains("Committed settings policy version 4.", this.harness.Console.Lines);
    }

    /// <summary>An organization's policy is read from and committed to that organization's route, and the deployment's own is never touched.</summary>
    [Fact]
    public async Task Edit_AnOrganizationNamed_CommitsToThatOrganizationsPolicyAlone()
    {
        // Arrange
        using var deployment = FakeSettingsPolicyDeployment.Holding(Organization, OneDefault);

        this.harness.EditsTheBufferInto(OneDefaultAndOneForcedValue);

        // Act
        var exitCode = await this.RunAsync(
            deployment, "policy", "edit", "--organization", $"{Organization:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.SettingsPolicyPath(Organization)));
        Assert.Empty(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.SettingsPolicyPath(organizationId: null)));
    }

    /// <summary>The first policy a scope is given is composed over version zero, which is what the deployment answered an empty scope with.</summary>
    [Fact]
    public async Task Edit_AScopeStoringNoPolicy_OpensTheEmptyDocumentAndCommitsOverVersionZero()
    {
        // Arrange
        using var deployment = FakeSettingsPolicyDeployment.StoringNoPolicy();

        var opened = string.Empty;
        this.harness.OpensTheBufferWith((_, path) =>
        {
            opened = File.ReadAllText(path);
            File.WriteAllText(path, OneDefault);

            return true;
        });

        // Act
        var exitCode = await this.RunAsync(deployment, "policy", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal("{}", opened);

        var sent = Assert.Single(
            deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.SettingsPolicyPath(organizationId: null)));

        Assert.Equal(0, ReadVersion(sent.ContentAsUtf8String()));
    }

    /// <summary>A policy saved back as it was opened is nothing to commit, and spending a version on it would be a change the operator did not ask for.</summary>
    [Fact]
    public async Task Edit_ABufferSavedUnchanged_WritesNothing()
    {
        // Arrange
        using var deployment = FakeSettingsPolicyDeployment.Holding(organization: null, OneDefault);

        this.harness.EditsTheBufferInto(OneDefault);

        // Act
        var exitCode = await this.RunAsync(deployment, "policy", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.SettingsPolicyPath(organizationId: null)));
    }

    /// <summary>
    /// A buffer that differs from what was opened only in how it is written is posted, and the deployment is what finds
    /// it states nothing new. That is not a refusal, so its sentence is printed as an answer and the invocation succeeds.
    /// </summary>
    [Fact]
    public async Task Edit_ASaveTheDeploymentFindsUnchanged_SaysSoAndSucceeds()
    {
        // Arrange
        const string unchanged = "The saved policy states what the policy in force already states, so nothing was written and version 3 stays in force.";

        using var deployment = FakeSettingsPolicyDeployment.FindingNothingToChange(organization: null, unchanged, OneDefault);

        this.harness.EditsTheBufferInto("""{ "Users": { "Defaults": { "Language": "pl" } } }""");

        // Act
        var exitCode = await this.RunAsync(deployment, "policy", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.SettingsPolicyPath(organizationId: null)));
        Assert.Contains(unchanged, this.harness.Console.Lines);
        Assert.Empty(this.harness.Console.Failures);
        Assert.Empty(this.harness.Console.Errors);
    }

    /// <summary>An emptied buffer abandons the session, and the sentence saying so names whose policy was left alone.</summary>
    [Fact]
    public async Task Edit_AnEmptiedBuffer_LeavesThePolicyAsItWasAndSaysWhoseItIs()
    {
        // Arrange
        using var deployment = FakeSettingsPolicyDeployment.Holding(Organization, OneDefault);

        this.harness.EditsTheBufferInto(string.Empty);

        // Act
        var exitCode = await this.RunAsync(
            deployment, "policy", "edit", "--organization", $"{Organization:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.SettingsPolicyPath(Organization)));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("this organization's settings policy was left as it was", StringComparison.Ordinal));
    }

    /// <summary>The YAML view is a rendering of the policy, and what is committed is the JSON document it describes.</summary>
    [Fact]
    public async Task Edit_AYamlView_CommitsTheEditAsJson()
    {
        // Arrange
        using var deployment = FakeSettingsPolicyDeployment.Holding(organization: null, OneDefault);

        this.harness.EditsTheBufferInto(
            """
            Users:
              Defaults:
                Language: en
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "policy", "edit", "--format", "yaml", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var sent = Assert.Single(
            deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.SettingsPolicyPath(organizationId: null)));
        using var committed = JsonDocument.Parse(ReadDocument(sent.ContentAsUtf8String()));

        Assert.Equal(
            "en",
            committed.RootElement.GetProperty("Users").GetProperty("Defaults").GetProperty("Language").GetString());
    }

    /// <summary>Nothing is opened without an editor to open it in, and no policy is fetched for a session that cannot start.</summary>
    [Fact]
    public async Task Edit_NoEditorNamedByTheShell_FailsWithoutReadingThePolicy()
    {
        // Arrange
        using var deployment = FakeSettingsPolicyDeployment.Holding(organization: null, OneDefault);

        // Act
        var exitCode = await this.RunAsync(deployment, "policy", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains(OperatorEditor.VisualVariable, StringComparison.Ordinal));
        Assert.Empty(deployment.RecordedRequests);
    }

    /// <summary>A refusal is the deployment's own sentence, printed as a failure, and the invocation ends as one.</summary>
    [Fact]
    public async Task Edit_APolicyTheDeploymentRefuses_FailsWithTheSentenceItGave()
    {
        // Arrange
        const string refusal = "Users:Defaults:Dialect names no property of a user's record.";

        using var deployment = FakeSettingsPolicyDeployment.RefusingTheWrite(
            organization: null,
            CandidateInvalid,
            refusal,
            OneDefault);

        this.harness.EditsTheBufferInto("""{"Users":{"Defaults":{"Dialect":"pl"}}}""");

        // Act
        var exitCode = await this.RunAsync(deployment, "policy", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal([refusal], this.harness.Console.Failures);
        Assert.Single(deployment.RequestsTo(HttpMethod.Get, AdminEndpointRoutes.SettingsPolicyPath(organizationId: null)));
    }

    /// <summary>
    /// A policy somebody else committed over is refused rather than merged, and what an operator has to see is what that
    /// writer changed, as the paths they re-apply their own change in.
    /// </summary>
    [Fact]
    public async Task Edit_AVersionAnotherWriterMovedPast_ReportsTheSettingsThatMoved()
    {
        // Arrange
        using var deployment = FakeSettingsPolicyDeployment.RefusingTheWrite(
            organization: null,
            VersionSuperseded,
            "The policy was composed over version 3 and version 4 is in force.",
            OneDefault,
            OneDefaultAndOneForcedValue);

        this.harness.EditsTheBufferInto("""{"Users":{"Defaults":{"Language":"en"}}}""");

        // Act
        var exitCode = await this.RunAsync(deployment, "policy", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Failures,
            line => line.Contains("version 4 is in force", StringComparison.Ordinal));
        Assert.Contains("These settings differ between version 3 and version 4:", this.harness.Console.Errors);
        Assert.Contains("  Users:Forced:TimeZone", this.harness.Console.Errors);
    }

    /// <summary>
    /// A policy is never redacted, so a version that reads the same as the one the buffer was opened over is the same,
    /// and the operator is told the policy in force already states it rather than sent looking for a hidden value.
    /// </summary>
    [Fact]
    public async Task Edit_AVersionAnotherWriterMovedPastWithoutChangingASetting_SaysThePolicyInForceAlreadyStatesIt()
    {
        // Arrange
        using var deployment = FakeSettingsPolicyDeployment.RefusingTheWrite(
            organization: null,
            VersionSuperseded,
            "The policy was composed over version 3 and version 4 is in force.",
            OneDefault,
            OneDefault);

        this.harness.EditsTheBufferInto(OneDefaultAndOneForcedValue);

        // Act
        var exitCode = await this.RunAsync(deployment, "policy", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);

        var notice = Assert.Single(
            this.harness.Console.Errors,
            line => line.Contains("the buffer was opened over", StringComparison.Ordinal));

        Assert.StartsWith("Version 4 carries the same settings", notice, StringComparison.Ordinal);
        Assert.Contains("the policy in force already states", notice, StringComparison.Ordinal);
        Assert.DoesNotContain("redact", notice, StringComparison.Ordinal);
    }

    public void Dispose() => this.harness.Dispose();

    private static string ReadDocument(string body)
    {
        using var request = JsonDocument.Parse(body);

        return request.RootElement.GetProperty("document").GetString() ?? string.Empty;
    }

    private static long ReadVersion(string body)
    {
        using var request = JsonDocument.Parse(body);

        return request.RootElement.GetProperty("version").GetInt64();
    }

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);
}
