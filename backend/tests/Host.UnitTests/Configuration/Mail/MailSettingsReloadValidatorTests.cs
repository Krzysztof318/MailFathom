// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.Configuration.Persistence;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers what a reloaded mail declaration has to prove before synchronization is run through it.</summary>
/// <remarks>
/// The deployment's own <c>Accounts</c> section names no owner, so a start refuses it beside owner declarations. The
/// same file arriving as a reload would otherwise be adopted, and the lookup that resolves a configured account reads
/// that section first — so an owner's mailbox would be run under settings that belong to nobody.
/// </remarks>
public sealed class MailSettingsReloadValidatorTests
{
    [Fact]
    public async Task FindConfigurationErrorsAsync_TheDeploymentSectionOnADeploymentServingDeclaredOwners_IsRefused()
    {
        // Arrange
        var roster = ResolvedServedMailOwners.Declaring(SyntheticMailOwner.Deployment, "alex");
        var validator = new MailSettingsReloadValidator(SecretValidator(), roster);

        // Act
        var errors = await validator.FindConfigurationErrorsAsync(DeclaringOneAccount(), CancellationToken.None);

        // Assert
        Assert.Contains(
            errors,
            error => error.StartsWith("MailSynchronization:Accounts declares 1 mail accounts", StringComparison.Ordinal));
    }

    /// <summary>That section belongs to the one owner a deployment declaring none holds, so it is the ordinary shape rather than a conflict.</summary>
    [Fact]
    public async Task FindConfigurationErrorsAsync_TheDeploymentSectionOnADeploymentServingItsSoleOwner_IsAdoptable()
    {
        // Arrange
        var validator = new MailSettingsReloadValidator(
            SecretValidator(),
            ResolvedServedMailOwners.TheSoleOwner());

        // Act
        var errors = await validator.FindConfigurationErrorsAsync(DeclaringOneAccount(), CancellationToken.None);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// The reload validation runs from the first gate onwards, which is ahead of the gate that settles the roster, so a
    /// candidate judged in that window is judged against a deployment that serves nobody yet rather than refused.
    /// </summary>
    [Fact]
    public async Task FindConfigurationErrorsAsync_ACandidateJudgedBeforeTheRosterIsSettled_IsAdoptable()
    {
        // Arrange
        var validator = new MailSettingsReloadValidator(SecretValidator(), new ServedMailOwners());

        // Act
        var errors = await validator.FindConfigurationErrorsAsync(DeclaringOneAccount(), CancellationToken.None);

        // Assert
        Assert.Empty(errors);
    }

    private static MailSynchronizationOptions DeclaringOneAccount() => new()
    {
        Accounts =
        [
            new MailSynchronizationAccountOptions
            {
                AccountId = "work",
                DisplayName = "Work",
                Host = "imap.example.test",
                UserName = "alex@example.test",
            },
        ],
    };

    private static SecretConfigurationValidator SecretValidator()
    {
        var resolver = new PlaintextOnlySecretReferenceResolver();

        return new SecretConfigurationValidator(
            resolver,
            new TrustAnchorLoader(resolver),
            new DatabaseConnectionSettingsMapper(new ConfigurationBuilder().Build()),
            new StubDatabaseConnectionSettingsValidator(),
            PostgresTextSearchConfiguration.Default,
            new DatabaseCommandTimeout(TimeSpan.FromSeconds(HostApplicationBuilderExtensions.DefaultDatabaseCommandTimeoutSeconds)),
            new FakeTimeProvider(),
            new RecordingLogger<SecretConfigurationValidator>());
    }
}
