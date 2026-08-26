// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Host.Configuration.RootSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
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
    private static readonly DateTimeOffset Today = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A configuration naming nothing is what a deployment that configured nothing runs, so it is usable.</summary>
    [Fact]
    public void FindErrors_AConfigurationNamingNothing_FindsNothing()
    {
        // Arrange
        var validator = Validator();

        // Act
        var errors = validator.FindErrors(Compose(new()));

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A property no section defines is refused by the strict binding, and the message names the key.</summary>
    [Fact]
    public void FindErrors_APropertyNoSectionDefines_NamesIt()
    {
        // Arrange
        var validator = Validator();

        // Act
        var errors = validator.FindErrors(Compose(new() { ["MailboxSearch:SnippetsPerEmails"] = "3" }));

        // Assert
        Assert.Contains(errors, error => error.Contains("SnippetsPerEmails", StringComparison.Ordinal));
    }

    /// <summary>A value outside the range its data annotation states is refused.</summary>
    [Fact]
    public void FindErrors_AValueOutsideItsRange_NamesTheSetting()
    {
        // Arrange
        var validator = Validator();

        // Act
        var errors = validator.FindErrors(Compose(new() { ["MailboxSearch:SnippetsPerEmail"] = "-1" }));

        // Assert
        Assert.Contains(errors, error => error.Contains("SnippetsPerEmail", StringComparison.Ordinal));
    }

    /// <summary>
    /// A rule that needs the current date is refused by the custom validator the host registers for it, which proves
    /// the candidate container constructs that validator with the clock the running process has.
    /// </summary>
    [Fact]
    public void FindErrors_ASynchronizationWindowAheadOfTheClock_NamesTheAccount()
    {
        // Arrange
        var validator = Validator();

        // Act
        var errors = validator.FindErrors(Compose(new()
        {
            ["MailSynchronization:Accounts:0:AccountId"] = "work",
            ["MailSynchronization:Accounts:0:EarliestEmailReceivedDate"] = "2030-01-01",
        }));

        // Assert
        Assert.Contains(errors, error => error.Contains("2030-01-01", StringComparison.Ordinal));
    }

    /// <summary>
    /// A scanner switched on with nothing behind it is refused by the catalog validator, which proves the candidate
    /// container constructs that one too — with the detectors this deployment registered rather than with none.
    /// </summary>
    [Fact]
    public void FindErrors_AScannerNoRegisteredDetectorServes_NamesIt()
    {
        // Arrange
        var validator = Validator();

        // Act
        var errors = validator.FindErrors(Compose(new() { ["SensitiveContent:Secrets:Enabled"] = "true" }));

        // Assert
        Assert.Contains(errors, error => error.Contains("no detector", StringComparison.Ordinal));
    }

    /// <summary>The same scanner is usable where a detector serves it, so the rule reads the catalogs it was given.</summary>
    [Fact]
    public void FindErrors_AScannerARegisteredDetectorServes_FindsNothing()
    {
        // Arrange
        var validator = Validator(new StubSensitiveContentCatalog(
            SensitiveContentScannerKind.Secrets,
            [StubSensitiveContentCatalog.Declare("Credentials", detectedByDefault: true, "ApiKey")]));

        // Act
        var errors = validator.FindErrors(Compose(new() { ["SensitiveContent:Secrets:Enabled"] = "true" }));

        // Assert
        Assert.Empty(errors);
    }

    private static CandidateSettingsValidator Validator(params ISensitiveContentCatalog[] catalogs) =>
        new(new FakeTimeProvider(Today), catalogs);

    private static IConfiguration Compose(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
