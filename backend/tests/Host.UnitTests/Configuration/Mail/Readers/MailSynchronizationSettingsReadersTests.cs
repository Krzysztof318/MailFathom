// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail.Readers;

/// <summary>Covers the lifetime the port readers are built with, which is what their memoized maps cost or save.</summary>
/// <remarks>
/// Every reader is registered as a scoped forwarder to the snapshot's own set, so a run unit and every message it
/// reads share one build. These tests hold that: an answer built once is observed as the same instance from two
/// scopes, and a snapshot handed down by an enclosing run answers from its own set rather than from the published one.
/// </remarks>
public sealed class MailSynchronizationSettingsReadersTests
{
    /// <summary>The readers belong to the snapshot, so every scope over one snapshot reads through one set.</summary>
    [Fact]
    public void Readers_TwoScopesOverOnePublishedSnapshot_ShareOneSet()
    {
        // Arrange
        var published = new StubSettingsSnapshot<MailSynchronizationOptions>(
            OptionsFor(AccountAt("work", "owner@work.example")));

        // Act
        var first = new ScopedMailSynchronizationSettings(published).Current.Readers;
        var second = new ScopedMailSynchronizationSettings(published).Current.Readers;

        // Assert
        Assert.Same(first, second);
    }

    /// <summary>Every arriving message asks for a policy, so the map behind it is built once rather than once per scope.</summary>
    [Fact]
    public void GetTrustPolicy_AskedFromTwoScopesOfOneSnapshot_AnswersFromOneBuild()
    {
        // Arrange
        var published = new StubSettingsSnapshot<MailSynchronizationOptions>(
            OptionsFor(AccountAt("work", "owner@work.example")));
        var work = MailAccountId.Create("work");

        // Act
        var first = new ScopedMailSynchronizationSettings(published).Current
            .Readers.SenderTrustPolicies.GetTrustPolicy(work);
        var second = new ScopedMailSynchronizationSettings(published).Current
            .Readers.SenderTrustPolicies.GetTrustPolicy(work);

        // Assert
        Assert.Same(first, second);
    }

    /// <summary>Collection settings are read per message as well, and are memoized for the same reason.</summary>
    [Fact]
    public void GetContactCollectionSettings_AskedFromTwoScopesOfOneSnapshot_AnswersFromOneBuild()
    {
        // Arrange
        var account = AccountAt("work", "owner@work.example");
        account.ContactCollection = new ContactCollectionOptions { Enabled = true };
        var published = new StubSettingsSnapshot<MailSynchronizationOptions>(OptionsFor(account));
        var work = MailAccountId.Create("work");

        // Act
        var first = new ScopedMailSynchronizationSettings(published).Current
            .Readers.ContactCollection.GetContactCollectionSettings(work);
        var second = new ScopedMailSynchronizationSettings(published).Current
            .Readers.ContactCollection.GetContactCollectionSettings(work);

        // Assert
        Assert.Same(first, second);
    }

    /// <summary>A reload binds a new snapshot, which brings a set of its own rather than the superseded one's answers.</summary>
    [Fact]
    public void Readers_AReloadPublishingANewSnapshot_BuildItsOwnSet()
    {
        // Arrange
        var published = new StubSettingsSnapshot<MailSynchronizationOptions>(
            OptionsFor(AccountAt("work", "owner@work.example")));
        var beforeReload = new ScopedMailSynchronizationSettings(published).Current.Readers;

        // Act
        published.Current = OptionsFor(AccountAt("work", "owner@work.example"));
        var afterReload = new ScopedMailSynchronizationSettings(published).Current.Readers;

        // Assert
        Assert.NotSame(beforeReload, afterReload);
    }

    /// <summary>A run hands its snapshot down, so the scope reads that snapshot's accounts rather than the published list.</summary>
    [Fact]
    public void Readers_AScopeHandedTheRunsSnapshot_ReadTheAccountsOfThatSnapshot()
    {
        // Arrange
        var published = new StubSettingsSnapshot<MailSynchronizationOptions>(
            OptionsFor(AccountAt("added-by-a-reload", "owner@work.example")));
        var runSnapshot = OptionsFor(AccountAt("scheduled-by-the-run", "owner@work.example"));
        var scope = new ScopedMailSynchronizationSettings(published);

        // Act
        scope.UseRunSnapshot(runSnapshot);

        // Assert
        Assert.Same(runSnapshot.Readers, scope.Current.Readers);
        Assert.Equal(
            [MailAccountId.Create("scheduled-by-the-run")],
            ConfiguredMailAccounts.CatalogOver(scope.Current).ServedAccounts.Select(static account => account.Id));
    }

    private static MailSynchronizationOptions OptionsFor(params MailSynchronizationAccountOptions[] accounts) => new()
    {
        Accounts = [.. accounts],
    };

    private static MailSynchronizationAccountOptions AccountAt(string accountId, string userName) => new()
    {
        AccountId = accountId,
        DisplayName = $"Account {accountId}",
        Host = "imap.example.test",
        UserName = userName,
        Secrets = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = $"systemd-credential:imap-{accountId}-password" },
        },
    };
}
