// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Records;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>
/// Covers how a replica brings its roster up to the user records another replica committed. Each case is a replica that
/// heard no announcement at all, because the rows are the only thing a reading takes its answer from.
/// </summary>
public sealed class ServedMailUsersConvergenceTests
{
    /// <summary>The record a provisioning leaves behind, which declares nothing until its user asks for something.</summary>
    private const string EmptyRecord = "{}";

    private static readonly MailUserId Alex = MailUserId.Create(new Guid("33333333-3333-3333-3333-333333333333"));

    /// <summary>A mailbox assigned to Alex, which is a record of its own rather than part of Alex's.</summary>
    private static readonly MailAccountRecord WorkMailbox = new(
        new Guid("0197a3c0-0000-7000-8000-000000000001"),
        "alex@example.test",
        "work",
        """
        {
          "Host": "imap.example.test",
          "UserName": "alex@example.test",
          "Secrets": { "Password": { "Name": "imap-password", "SecretReference": "systemd-credential:imap-password" } }
        }
        """,
        Version: 1);

    /// <summary>A second mailbox of Alex's, which is what makes "one declaration costs itself" observable.</summary>
    private static readonly MailAccountRecord SpareMailbox = new(
        new Guid("0197a3c0-0000-7000-8000-000000000002"),
        "alex.spare@example.test",
        "spare",
        """
        {
          "Host": "imap.example.test",
          "UserName": "alex.spare@example.test",
          "Secrets": { "Password": { "Name": "imap-password", "SecretReference": "systemd-credential:imap-password" } }
        }
        """,
        Version: 1);

    /// <summary>A record committed on another replica is published here at the version the row holds, with the accounts assigned beside it.</summary>
    [Fact]
    public async Task ConvergeAsync_ARecordCommittedOnAnotherReplica_PublishesItsNewerVersion()
    {
        // Arrange
        var roster = ServingAlexAt(version: 2);
        var documents = Holding([new UserSettingsDocument(Alex, "alex", EmptyRecord, 3) { MailAccounts = [WorkMailbox] }]);

        // Act
        await Convergence(documents, roster).ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        var served = Assert.Single(roster.Users);
        Assert.Equal([WorkMailbox.Id.ToString("D")], served.MailAccounts.Select(account => account.AccountId));
        Assert.Equal(3, roster.PublishedVersionOf(Alex));
    }

    /// <summary>
    /// A record already bound at the version the row holds is not read again, which is what keeps a reading between two
    /// changes to one statement however many users the deployment serves.
    /// </summary>
    [Fact]
    public async Task ConvergeAsync_EveryRecordAtTheVersionBound_ReadsNoRecord()
    {
        // Arrange
        var roster = ServingAlexAt(version: 2);
        var documents = Holding((Alex, EmptyRecord, 2));

        // Act
        await Convergence(documents, roster).ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        await documents.DidNotReceive().ReadAsync(Arg.Any<MailUserId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A user another replica recorded is served here, under the label their row carries.</summary>
    [Fact]
    public async Task ConvergeAsync_AUserRecordedOnAnotherReplica_ServesThem()
    {
        // Arrange
        var roster = new ServedMailUsers();
        roster.Resolved([]);
        var documents = Holding((Alex, EmptyRecord, 2));

        // Act
        await Convergence(documents, roster).ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        var served = Assert.Single(roster.Users);
        Assert.Equal(Alex, served.User);
        Assert.Equal("alex", served.DisplayName);
    }

    /// <summary>A user another replica erased is no longer served here, since a row that is gone is the whole of what an erasure leaves.</summary>
    [Fact]
    public async Task ConvergeAsync_AUserErasedOnAnotherReplica_DropsThemFromTheRoster()
    {
        // Arrange
        var roster = ServingAlexAt(version: 2);
        var documents = Holding();

        // Act
        await Convergence(documents, roster).ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(roster.Users);
    }

    /// <summary>
    /// A record that does not bind is not published, the version bound before it stays in force, and it is reported once
    /// rather than on every interval the same row is read at the same version.
    /// </summary>
    [Fact]
    public async Task ConvergeAsync_ARecordThatDoesNotBind_KeepsTheVersionBoundAndReportsItOnce()
    {
        // Arrange
        var roster = ServingAlexAt(version: 2);
        var documents = Holding((Alex, """{"NothingBindsThis":true}""", 3));
        var log = new RecordingLogger<ServedMailUsersConvergence>();
        var heldBack = new HeldBackRecords();
        var convergence = Convergence(documents, roster, log, heldBack);

        // Act
        await convergence.ConvergeAsync(TestContext.Current.CancellationToken);
        await convergence.ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, roster.PublishedVersionOf(Alex));
        Assert.Single(log.Messages, message => message.Contains("at version 3", StringComparison.Ordinal));
        var refused = Assert.Single(heldBack.Current);
        Assert.Equal(HeldBackRecordKind.User, refused.Kind);
        Assert.Equal(Alex.Value, refused.Identity);
        Assert.Equal(3, refused.RejectedVersion);
    }

    /// <summary>
    /// One mail account this build will not bind costs that account alone: the rest of the user's mailboxes are
    /// republished at the version the row holds, so a single broken declaration never freezes a user's whole record.
    /// </summary>
    [Fact]
    public async Task ConvergeAsync_ARecordWhoseOneMailboxDoesNotBind_PublishesTheRestAtTheNewVersion()
    {
        // Arrange
        var roster = ServingAlexAt(version: 2);
        var heldBack = new HeldBackRecords();
        var unbindable = SpareMailbox with
        {
            Document = """{"Host":"imap.example.test","NothingBindsThis":true}""",
        };
        var documents = Holding(
            [new UserSettingsDocument(Alex, "alex", EmptyRecord, 3) { MailAccounts = [WorkMailbox, unbindable] }]);

        // Act
        await Convergence(documents, roster, heldBack: heldBack)
            .ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        var served = Assert.Single(roster.Users);
        Assert.Equal([WorkMailbox.Id.ToString("D")], served.MailAccounts.Select(account => account.AccountId));
        Assert.Equal(3, roster.PublishedVersionOf(Alex));
        Assert.Equal(unbindable.Id, Assert.Single(heldBack.Current).Identity);
    }

    /// <summary>A record repaired on another replica stops being held back here, so nobody is told to correct a row that is already correct.</summary>
    [Fact]
    public async Task ConvergeAsync_ARecordRepairedOnAnotherReplica_StopsHoldingItBack()
    {
        // Arrange
        var roster = ServingAlexAt(version: 2);
        var heldBack = new HeldBackRecords();

        heldBack.Replace(Alex, [new HeldBackRecord(HeldBackRecordKind.User, Alex.Value, "alex", 3, ["stale"])]);

        // Act
        await Convergence(Holding((Alex, EmptyRecord, 4)), roster, heldBack: heldBack)
            .ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4, roster.PublishedVersionOf(Alex));
        Assert.Empty(heldBack.Current);
    }

    /// <summary>A user erased on another replica takes whatever was held back about them with them.</summary>
    [Fact]
    public async Task ConvergeAsync_AUserErasedOnAnotherReplica_StopsHoldingTheirRecordsBack()
    {
        // Arrange
        var roster = ServingAlexAt(version: 2);
        var heldBack = new HeldBackRecords();

        heldBack.Replace(Alex, [new HeldBackRecord(HeldBackRecordKind.User, Alex.Value, "alex", 2, ["stale"])]);

        // Act
        await Convergence(Holding(), roster, heldBack: heldBack)
            .ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(roster.Users);
        Assert.Empty(heldBack.Current);
    }

    /// <summary>A replica whose startup gate has not settled a roster reads nothing, because that gate composes the whole roster from the rows itself.</summary>
    [Fact]
    public async Task ConvergeAsync_BeforeTheStartupGateSettledARoster_ReadsNothing()
    {
        // Arrange
        var documents = Holding((Alex, EmptyRecord, 2));

        // Act
        await Convergence(documents, new ServedMailUsers()).ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        await documents.DidNotReceive().ReadVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private static ServedMailUsers ServingAlexAt(long version)
    {
        var roster = new ServedMailUsers();

        roster.Resolved([new ServedMailUser(Alex, "alex", [])], new Dictionary<MailUserId, long> { [Alex] = version });

        return roster;
    }

    /// <summary>The rows a deployment holds: each user's version, and the record a read of that user answers with.</summary>
    private static IUserSettingsDocumentReader Holding(params (MailUserId User, string Json, long Version)[] records) =>
        Holding([.. records.Select(record => new UserSettingsDocument(record.User, "alex", record.Json, record.Version))]);

    private static IUserSettingsDocumentReader Holding(IReadOnlyList<UserSettingsDocument> records)
    {
        var documents = Substitute.For<IUserSettingsDocumentReader>();

        documents.ReadVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([.. records.Select(record => new UserSettingsDocumentVersion(record.User, record.Version))]);

        foreach (var record in records)
        {
            documents.ReadAsync(record.User, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<UserSettingsDocument?>(record));
        }

        return documents;
    }

    private static ServedMailUsersConvergence Convergence(
        IUserSettingsDocumentReader documents,
        ServedMailUsers roster,
        RecordingLogger<ServedMailUsersConvergence>? log = null,
        HeldBackRecords? heldBack = null) =>
        new(
            UserRecordScopes.Resolving(
                documents,
                new UserAccountDocumentBinder(
                    new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
                    new FakeTimeProvider(),
                    Options.Create(new SensitiveContentOptions()))),
            roster,
            heldBack ?? new HeldBackRecords(),
            log ?? new RecordingLogger<ServedMailUsersConvergence>());
}
