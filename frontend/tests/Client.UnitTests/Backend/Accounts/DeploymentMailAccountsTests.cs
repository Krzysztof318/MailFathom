// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Accounts;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Backend.Accounts;

/// <summary>What the client makes of the accounts a deployment reports, and of the ways it can decline to report any.</summary>
public sealed class DeploymentMailAccountsTests
{
    private const string TwoAccounts =
        """
        {
          "synchronizationEnabled": true,
          "accounts": [
            {
              "id": "work",
              "displayName": "Work mail",
              "synchronizationState": "Synchronized",
              "lastSynchronizedAt": "2026-08-15T10:00:00+00:00",
              "behind": false
            },
            {
              "id": "private",
              "displayName": "Private mail",
              "synchronizationState": "Failing",
              "lastSynchronizedAt": null,
              "behind": true
            }
          ]
        }
        """;

    /// <summary>Every field of the contract is read, because the tree shows each of them about a mailbox.</summary>
    [Fact]
    public async Task ReadMailAccountsAsync_ADeploymentAnswering_ReadsEveryFieldOfTheContract()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(_ => StubTransport.JsonResponse(TwoAccounts));

        // Act
        var answered = await harness.Client.ReadMailAccountsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(answered.SynchronizationEnabled);
        Assert.Equal(2, answered.Owned.Count);

        var work = answered.Owned[0];
        Assert.Equal("work", work.Id);
        Assert.Equal("Work mail", work.DisplayName);
        Assert.Equal(MailSynchronizationStanding.Synchronized, work.Standing);
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero), work.LastSynchronizedAt);
        Assert.False(work.Behind);

        var privateMail = answered.Owned[1];
        Assert.Equal(MailSynchronizationStanding.Failing, privateMail.Standing);
        Assert.Null(privateMail.LastSynchronizedAt);
        Assert.True(privateMail.Behind);
    }

    /// <summary>The route is the client surface's own, rather than the administrative one that serves a different reader.</summary>
    [Fact]
    public async Task ReadMailAccountsAsync_AnyRequest_GoesToTheClientSurface()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(_ => StubTransport.JsonResponse(TwoAccounts));

        // Act
        await harness.Client.ReadMailAccountsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            new Uri("https://mail.example/api/client/accounts"),
            Assert.Single(harness.Deployment.Requests).RequestUri);
    }

    /// <summary>An owner who owns nothing is a state to render, and it arrives as an empty list rather than as a failure.</summary>
    [Fact]
    public async Task ReadMailAccountsAsync_AnOwnerWhoOwnsNoAccount_ReadsAnEmptyListRatherThanFailing()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse("""{"synchronizationEnabled":true,"accounts":[]}"""));

        // Act
        var answered = await harness.Client.ReadMailAccountsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(answered.Owned);
    }

    /// <summary>
    /// A document naming no accounts at all reads the same way an empty list does, so no screen has to remember which
    /// of the two shapes it received.
    /// </summary>
    [Fact]
    public async Task Owned_ADocumentNamingNoAccounts_ReadsAsAnOwnerWhoOwnsNone()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse("""{"synchronizationEnabled":false}"""));

        // Act
        var answered = await harness.Client.ReadMailAccountsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(answered.Owned);
        Assert.False(answered.SynchronizationEnabled);
    }

    /// <summary>
    /// A credential the deployment will not serve is refused rather than answered with nothing, which is what keeps
    /// it from reading as an owner who owns no mailbox.
    /// </summary>
    [Fact]
    public async Task ReadMailAccountsAsync_ACredentialWithoutTheGrant_IsRefusedRatherThanAnsweredWithNothing()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse("{}", HttpStatusCode.Forbidden));

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.ReadMailAccountsAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.CredentialRefused, failure.Reason);
    }

    /// <summary>The four names the contract publishes are the four the client knows, matched exactly.</summary>
    [Theory]
    [InlineData("NeverSynchronized", MailSynchronizationStanding.NeverSynchronized)]
    [InlineData("Synchronized", MailSynchronizationStanding.Synchronized)]
    [InlineData("Failing", MailSynchronizationStanding.Failing)]
    [InlineData("Unreachable", MailSynchronizationStanding.Unreachable)]
    public void Standing_ANamePublishedByTheContract_IsReadAsItself(
        string published,
        MailSynchronizationStanding expected)
    {
        // Arrange
        var account = new DeploymentMailAccount(
            "work",
            "Work mail",
            published,
            LastSynchronizedAt: null,
            Behind: false);

        // Act, Assert
        Assert.Equal(expected, account.Standing);
    }

    /// <summary>
    /// A name this build does not know claims nothing about the copy. Reading it as synchronized would tell somebody
    /// their mail is current on the strength of a word the client cannot interpret, and the major version is zero, so
    /// a deployment publishing a fifth standing before a client understands it is an ordinary case.
    /// </summary>
    [Theory]
    [InlineData("Paused")]
    [InlineData("synchronized")]
    [InlineData("2")]
    [InlineData("")]
    public void Standing_ANameThisClientDoesNotKnow_ClaimsNothingAboutTheCopy(string unknown)
    {
        // Arrange
        var account = new DeploymentMailAccount(
            "work",
            "Work mail",
            unknown,
            LastSynchronizedAt: null,
            Behind: false);

        // Act, Assert
        Assert.Equal(MailSynchronizationStanding.Unrecognized, account.Standing);
    }
}
