// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Host.Configuration.RootSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.RootSettings;

/// <summary>
/// Covers that a candidate configuration is judged by the rules a start applies, and by all of them: the strict
/// binding, the data annotations, and each custom validator the host registers. A validator the candidate container
/// could not construct would report nothing, which is the failure this suite exists to make loud — every case below
/// names a rule that lives in a different mechanism.
/// </summary>
public sealed class CandidateSettingsValidatorTests
{
    private static readonly Guid ServedAccount = new("0199a0c0-0000-7000-8000-00000000000a");

    private static readonly Guid UnservedAccount = new("0199a0c0-0000-7000-8000-00000000000b");

    /// <summary>A configuration naming nothing is what a deployment that configured nothing runs, so it is usable.</summary>
    [Fact]
    public async Task FindErrorsAsync_AConfigurationNamingNothing_FindsNothing()
    {
        // Arrange
        var validator = Validator();

        // Act
        var errors = await validator.FindErrorsAsync(Compose(new()), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A property no section defines is refused by the strict binding, and the message names the key.</summary>
    [Fact]
    public async Task FindErrorsAsync_APropertyNoSectionDefines_NamesIt()
    {
        // Arrange
        var validator = Validator();

        // Act
        var errors = await validator.FindErrorsAsync(Compose(new() { ["MailboxSearch:SnippetsPerEmails"] = "3" }), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(errors, error => error.Contains("SnippetsPerEmails", StringComparison.Ordinal));
    }

    /// <summary>A value outside the range its data annotation states is refused.</summary>
    [Fact]
    public async Task FindErrorsAsync_AValueOutsideItsRange_NamesTheSetting()
    {
        // Arrange
        var validator = Validator();

        // Act
        var errors = await validator.FindErrorsAsync(Compose(new() { ["MailboxSearch:SnippetsPerEmail"] = "-1" }), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(errors, error => error.Contains("SnippetsPerEmail", StringComparison.Ordinal));
    }

    /// <summary>
    /// A scanner switched on with nothing behind it is refused by the catalog validator, which proves the candidate
    /// container constructs that one too — with the detectors this deployment registered rather than with none.
    /// </summary>
    [Fact]
    public async Task FindErrorsAsync_AScannerNoRegisteredDetectorServes_NamesIt()
    {
        // Arrange
        var validator = Validator();

        // Act
        var errors = await validator.FindErrorsAsync(Compose(new() { ["SensitiveContent:Secrets:Enabled"] = "true" }), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(errors, error => error.Contains("no detector", StringComparison.Ordinal));
    }

    /// <summary>The same scanner is usable where a detector serves it, so the rule reads the catalogs it was given.</summary>
    [Fact]
    public async Task FindErrorsAsync_AScannerARegisteredDetectorServes_FindsNothing()
    {
        // Arrange
        var validator = Validator(new StubSensitiveContentCatalog(
            SensitiveContentScannerKind.Secrets,
            [StubSensitiveContentCatalog.Declare("Credentials", detectedByDefault: true, "ApiKey")]));

        // Act
        var errors = await validator.FindErrorsAsync(Compose(new() { ["SensitiveContent:Secrets:Enabled"] = "true" }), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// Two bound sections refused at once are both named, which is the promise a refused write makes to an operator
    /// correcting one setting at a time — and the only case in which the framework reports its failures as an
    /// aggregate rather than as one.
    /// </summary>
    [Fact]
    public async Task FindErrorsAsync_ACandidateTwoBoundSectionsBothRefuse_NamesBoth()
    {
        // Arrange
        var validator = Validator();

        // Act
        var errors = await validator.FindErrorsAsync(
            Compose(new()
            {
                ["MailboxSearch:SnippetsPerEmail"] = "-1",
                ["MailSynchronization:MaxConcurrentAccounts"] = "0",
            }),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(errors, error => error.Contains("SnippetsPerEmail", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MaxConcurrentAccounts", StringComparison.Ordinal));
    }

    /// <summary>
    /// A candidate turning every surface off is refused, which is a rule a start takes before its container exists:
    /// the process would hold a socket and serve nothing on it, and no options validator can reach that.
    /// </summary>
    [Fact]
    public async Task FindErrorsAsync_ACandidateThatWouldServeNothing_NamesTheSurfaces()
    {
        // Arrange
        var validator = Validator();

        // Act
        var errors = await validator.FindErrorsAsync(
            Compose(new()
            {
                ["McpEndpoint:Enabled"] = "false",
                ["AdminEndpoint:Enabled"] = "false",
                ["ClientEndpoint:Enabled"] = "false",
                ["HealthEndpoints:Enabled"] = "false",
            }),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(errors, error => error.Contains("No network surface is enabled", StringComparison.Ordinal));
    }

    /// <summary>
    /// A rule condition the compiler refuses is refused here too, for the same reason: the declaration gate runs while
    /// the host is composing itself, so a write that escaped it would commit and stop the next start.
    /// </summary>
    [Fact]
    public async Task FindErrorsAsync_ARuleConditionThatWillNotCompile_NamesTheRule()
    {
        // Arrange
        var validator = Validator();

        // Act
        var errors = await validator.FindErrorsAsync(
            Compose(new()
            {
                ["MailRules:Rules:0:Name"] = "unreadable",
                ["MailRules:Rules:0:Condition"] = "NoSuchFact == (",
            }),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(errors, error => error.Contains("MailRules:Rules", StringComparison.Ordinal));
    }

    /// <summary>
    /// Composing the listeners reads each profile as though its own validator had already passed — a domain unique
    /// because validation proved it so — so a section that answered with a refusal is one whose declarations cannot be
    /// built at all. Two profiles that both omit a domain is that shape: each is refused on its own, and declaring them
    /// anyway would collide on the empty key and leave this port raising rather than refusing.
    /// </summary>
    [Fact]
    public async Task FindErrorsAsync_TwoHttpsProfilesTheSectionAlreadyRefuses_IsAnErrorRatherThanAnException()
    {
        // Arrange
        var validator = Validator();

        // Act
        var errors = await validator.FindErrorsAsync(
            Compose(new()
            {
                ["McpEndpoint:Enabled"] = "true",
                ["McpEndpoint:Transport"] = "HttpAndHttps",
                ["McpEndpoint:Https:Endpoints:0:Port"] = "8443",
                ["McpEndpoint:Https:Endpoints:1:Port"] = "8444",
            }),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(errors, error => error.Contains("McpEndpoint:Https:Endpoints", StringComparison.Ordinal));
    }

    /// <summary>
    /// The chat section is registered without <c>ValidateOnStart</c>, so the startup validator never materializes it
    /// and nothing in the throwaway container reads it strictly. A start still refuses a misspelled key there — the
    /// snapshot hosted service reads the monitor's current value while hosted services are resolved — so this reading
    /// is what has to take that refusal for a candidate.
    /// </summary>
    [Fact]
    public async Task FindErrorsAsync_AnUnknownKeyInTheChatSection_IsRefused()
    {
        // Arrange
        var validator = Validator();

        // Act
        var errors = await validator.FindErrorsAsync(
            Compose(new()
            {
                ["Chat:ModelName"] = "gpt-4o-mini",
            }),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(errors, error => error.Contains("ModelName", StringComparison.Ordinal));
    }

    /// <summary>
    /// A start reads the mail rules before it reads the providers, so a candidate faulty in both is refused for the
    /// rule first. The provider reading raises rather than returning where a key is misspelled, and the refusal a start
    /// would have reported is what has to survive that — otherwise a write answers the same candidate with the second
    /// of its two mistakes while a start answers with the first.
    /// </summary>
    [Fact]
    public async Task FindErrorsAsync_AMailRuleThatWillNotCompileBesideAnUnknownChatKey_KeepsTheRuleRefusal()
    {
        // Arrange
        var validator = Validator();

        // Act
        var errors = await validator.FindErrorsAsync(
            Compose(new()
            {
                ["MailRules:Rules:0:Name"] = "unreadable",
                ["MailRules:Rules:0:Condition"] = "NoSuchFact == (",
                ["Chat:ModelName"] = "gpt-4o-mini",
            }),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(errors, error => error.Contains("MailRules:Rules", StringComparison.Ordinal));
    }

    /// <summary>
    /// Part of what a start refuses over is decided while the sections are being registered rather than when they are
    /// validated. A resilience section naming no dependency class is that shape, and it is the same operator's mistake
    /// as a value a validator refuses — so it comes back as an error rather than as an exception the write raises.
    /// </summary>
    [Fact]
    public async Task FindErrorsAsync_AResilienceSectionNamingNoDependencyClass_IsAnErrorRatherThanAnException()
    {
        // Arrange
        var validator = Validator();

        // Act
        var errors = await validator.FindErrorsAsync(
            Compose(new()
            {
                ["Resilience:EmailDelivry:MaxAttempts"] = "3",
            }),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(errors, error => error.Contains("EmailDelivry", StringComparison.Ordinal));
    }

    /// <summary>
    /// A rule scoped to a mailbox no served record holds is refused by the write rather than committed as a rule set
    /// the reload would then refuse to apply, which is what judging it against the account records buys.
    /// </summary>
    [Fact]
    public async Task FindErrorsAsync_ARuleScopedToAMailboxNoServedRecordHolds_NamesTheMailbox()
    {
        // Arrange
        var validator = new CandidateSettingsValidator([], RecordsHolding(ServedAccount));

        // Act
        var errors = await validator.FindErrorsAsync(RuleScopedTo(UnservedAccount), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains($"mail account named '{UnservedAccount:D}'", StringComparison.Ordinal));
    }

    /// <summary>
    /// The control for the refusal above: a rule scoped to a mailbox a served record holds is written, so the records'
    /// mailboxes reach the judgement rather than an empty set that would refuse every scoped rule.
    /// </summary>
    [Fact]
    public async Task FindErrorsAsync_ARuleScopedToAMailboxAServedRecordHolds_FindsNothingAboutTheMailbox()
    {
        // Arrange
        var validator = new CandidateSettingsValidator([], RecordsHolding(ServedAccount));

        // Act
        var errors = await validator.FindErrorsAsync(RuleScopedTo(ServedAccount), TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(errors, error => error.Contains($"'{ServedAccount:D}'", StringComparison.Ordinal));
    }

    private static CandidateSettingsValidator Validator(params ISensitiveContentCatalog[] catalogs) =>
        new(catalogs, ServedMailAccountReaders.HoldingNothing());

    /// <summary>Serves one record per identifier a test names, each with a document that binds.</summary>
    private static IServedMailAccountReader RecordsHolding(params Guid[] accountIds) =>
        ServedMailAccountReaders.Holding(
        [
            .. accountIds.Select(accountId => new MailAccountRecord(accountId, "alex@example.test", "Alex at work", ServableMailAccountDocuments.Completing("{}"), Version: 1)),
        ]);

    /// <summary>A candidate declaring one rule, scoped to the mailbox identifier a test names.</summary>
    private static IConfiguration RuleScopedTo(Guid accountId) =>
        Compose(new()
        {
            ["MailRules:Rules:0:Name"] = "file-invoices",
            ["MailRules:Rules:0:Condition"] = "isSeen",
            ["MailRules:Rules:0:Accounts:0"] = accountId.ToString("D"),
        });

    private static IConfiguration Compose(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
