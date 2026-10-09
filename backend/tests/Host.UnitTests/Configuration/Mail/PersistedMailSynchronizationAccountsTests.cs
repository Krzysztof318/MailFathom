// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.Signals;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers how synchronization reads the accounts it supervises, and what one account's run holds, from the records.</summary>
public sealed class PersistedMailSynchronizationAccountsTests
{
    private static readonly UserId Alex = UserId.Create(new Guid("1a7f6b1c-2d3e-4f50-8a91-b2c3d4e5f601"));

    private static readonly UserId Morgan = UserId.Create(new Guid("2b8f7c2d-3e4f-4a61-9b02-c3d4e5f6a712"));

    private static readonly Guid AlexWork = new("0199a0c0-0000-7000-8000-00000000000a");

    private static readonly Guid AlexHome = new("0199a0c0-0000-7000-8000-00000000000b");

    private static readonly Guid MorganWork = new("0199a0c0-0000-7000-8000-00000000000c");

    /// <summary>A deployment larger than one page is read page by page, each one continuing after the last identifier read.</summary>
    [Fact]
    public async Task ReadSupervisedAsync_MoreAccountsThanOnePage_ReadsEveryPageAfterTheLastIdentifierOfTheOneBefore()
    {
        // Arrange
        var firstPage = Versions(PersistedMailSynchronizationAccounts.PageSize, offset: 0);
        var secondPage = Versions(1, offset: PersistedMailSynchronizationAccounts.PageSize);
        var harness = new SourceHarness();
        harness.Rows
            .ReadServedVersionsAsync(null, PersistedMailSynchronizationAccounts.PageSize, Arg.Any<CancellationToken>())
            .Returns(firstPage);
        harness.Rows
            .ReadServedVersionsAsync(firstPage[^1].Id, PersistedMailSynchronizationAccounts.PageSize, Arg.Any<CancellationToken>())
            .Returns(secondPage);

        // Act
        var supervised = await harness.Source.ReadSupervisedAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [.. firstPage.Concat(secondPage).Select(row => (row.Id.ToString("D"), row.Version))],
            supervised.Select(account => (account.Account.Value, account.Version)));
        await harness.Rows.ReceivedWithAnyArgs(2).ReadServedVersionsAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// An account an erasure is deciding about is left out of the pass, and the paging still continues after it, so the
    /// account behind a withheld one on a full page is not lost with it.
    /// </summary>
    [Fact]
    public async Task ReadSupervisedAsync_TheLastAccountOfAFullPageWithheld_LeavesItOutAndReadsTheNextPage()
    {
        // Arrange
        var firstPage = Versions(PersistedMailSynchronizationAccounts.PageSize, offset: 0);
        var secondPage = Versions(1, offset: PersistedMailSynchronizationAccounts.PageSize);
        var harness = new SourceHarness();
        harness.Rows
            .ReadServedVersionsAsync(null, PersistedMailSynchronizationAccounts.PageSize, Arg.Any<CancellationToken>())
            .Returns(firstPage);
        harness.Rows
            .ReadServedVersionsAsync(firstPage[^1].Id, PersistedMailSynchronizationAccounts.PageSize, Arg.Any<CancellationToken>())
            .Returns(secondPage);
        using var withholding = harness.Withheld.Withhold([MailAccountId.Create(firstPage[^1].Id.ToString("D"))]);

        // Act
        var supervised = await harness.Source.ReadSupervisedAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [.. firstPage.SkipLast(1).Concat(secondPage).Select(row => row.Id.ToString("D"))],
            supervised.Select(account => account.Account.Value));
    }

    /// <summary>
    /// A run holds its own account and the other accounts of the users it is assigned to, because what it derives from
    /// its neighbours is that person's own, and nothing of a user it is not assigned to.
    /// </summary>
    [Fact]
    public async Task ReadRunSettingsAsync_AnAccountOfOneUser_HoldsItAndThatUsersOtherAccountsOnly()
    {
        // Arrange
        var harness = new SourceHarness();
        harness.Assignments.Assigning(Alex, Account(AlexWork), Account(AlexHome)).Assigning(Morgan, Account(MorganWork));
        harness.Holding(Record(Alex, version: 3, Mailbox(AlexWork, "alex@work.test"), Mailbox(AlexHome, "alex@home.test")));
        harness.Holding(Record(Morgan, version: 7, Mailbox(MorganWork, "morgan@work.test")));

        // Act
        var runSettings = await harness.Source.ReadRunSettingsAsync(
            Account(AlexWork),
            previous: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(runSettings);
        Assert.Equal(
            [AlexWork.ToString("D"), AlexHome.ToString("D")],
            runSettings.DeclaredAccounts.Select(account => account.AccountId));
        Assert.NotNull(runSettings.FindConfiguredAccount(Account(AlexWork)));
        await harness.Documents.DidNotReceive().ReadAsync(Morgan, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Nothing a run was composed from changed, so it is handed the settings it already had; a changed record version is
    /// what composes new ones. The push watch keeps its sessions on exactly that reference.
    /// </summary>
    [Fact]
    public async Task ReadRunSettingsAsync_NothingChangedSinceThePreviousRun_ReturnsThePreviousSettingsUntilARecordMoves()
    {
        // Arrange
        var harness = new SourceHarness();
        harness.Assignments.Assigning(Alex, Account(AlexWork));
        harness.Holding(Record(Alex, version: 3, Mailbox(AlexWork, "alex@work.test")));
        var previous = await harness.Source.ReadRunSettingsAsync(
            Account(AlexWork),
            previous: null,
            TestContext.Current.CancellationToken);

        // Act
        var unchanged = await harness.Source.ReadRunSettingsAsync(
            Account(AlexWork),
            previous,
            TestContext.Current.CancellationToken);
        harness.Holding(Record(Alex, version: 4, Mailbox(AlexWork, "alex@work.test")));
        var changed = await harness.Source.ReadRunSettingsAsync(
            Account(AlexWork),
            unchanged,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(previous);
        Assert.Same(previous, unchanged);
        Assert.NotNull(changed);
        Assert.NotSame(previous, changed);
    }

    /// <summary>An account erased since the last run is assigned to nobody, so its run finds nothing and its supervision ends.</summary>
    [Fact]
    public async Task ReadRunSettingsAsync_AnAccountAssignedToNobody_AnswersNone()
    {
        // Arrange
        var harness = new SourceHarness();
        harness.Assignments.Assigning(Alex, Account(AlexHome));
        harness.Holding(Record(Alex, version: 3, Mailbox(AlexHome, "alex@home.test")));

        // Act
        var runSettings = await harness.Source.ReadRunSettingsAsync(
            Account(AlexWork),
            previous: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(runSettings);
    }

    /// <summary>An account an erasure is deciding about reads nothing, so no run starts writing to what is about to be deleted.</summary>
    [Fact]
    public async Task ReadRunSettingsAsync_AWithheldAccount_AnswersNoneWithoutReadingAnyRecord()
    {
        // Arrange
        var harness = new SourceHarness();
        harness.Assignments.Assigning(Alex, Account(AlexWork));
        harness.Holding(Record(Alex, version: 3, Mailbox(AlexWork, "alex@work.test")));
        using var withholding = harness.Withheld.Withhold([Account(AlexWork)]);

        // Act
        var runSettings = await harness.Source.ReadRunSettingsAsync(
            Account(AlexWork),
            previous: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(runSettings);
        await harness.Documents.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>Withholding an account and committing a change each wake the coordinator, which is what lets it act on either at once.</summary>
    [Fact]
    public async Task GetChangeToken_AnAccountWithheldOrAChangeAnnounced_Changes()
    {
        // Arrange
        var harness = new SourceHarness();
        var beforeWithholding = harness.Source.GetChangeToken();

        // Act
        using var withholding = harness.Withheld.Withhold([Account(AlexWork)]);
        var beforeAnnouncement = harness.Source.GetChangeToken();
        await harness.Announcements.AnnounceAsync();

        // Assert
        Assert.True(beforeWithholding.HasChanged);
        Assert.True(beforeAnnouncement.HasChanged);
    }

    private static MailAccountId Account(Guid id) => MailAccountId.Create(id.ToString("D"));

    private static ServedMailAccountVersion[] Versions(int count, int offset) =>
    [
        .. Enumerable.Range(offset, count)
            .Select(index => new ServedMailAccountVersion(new Guid($"0199a0c0-0000-7000-8000-{index:D12}"), index + 1L)),
    ];

    private static MailAccountRecord Mailbox(Guid id, string emailAddress) =>
        new(
            id,
            emailAddress,
            emailAddress,
            $$"""
              {
                "Host": "imap.example.test",
                "UserName": "{{emailAddress}}",
                "Secrets": { "Password": { "Name": "imap-password", "SecretReference": "systemd-credential:imap-password" } }
              }
              """,
            Version: 1);

    private static UserSettingsDocument Record(UserId user, long version, params MailAccountRecord[] accounts) =>
        new(user, $"user-{user.Value:D}", "{}", version) { MailAccounts = accounts };

    /// <summary>The source over records and assignments a test states.</summary>
    private sealed class SourceHarness
    {
        internal SourceHarness()
        {
            var binder = new UserAccountDocumentBinder(
                new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
                new FakeTimeProvider(),
                Options.Create(new SensitiveContentOptions()));

            this.Source = new PersistedMailSynchronizationAccounts(
                new StubSettingsSnapshot<MailSynchronizationOptions>(new MailSynchronizationOptions { Enabled = true }),
                this.Rows,
                this.Assignments,
                this.Documents,
                new ServedUserRecordComposition(binder),
                this.Announcements,
                this.Withheld);
        }

        internal IServedMailAccountReader Rows { get; } = Substitute.For<IServedMailAccountReader>();

        internal StubMailAccountAssignments Assignments { get; } = new();

        internal IUserSettingsDocumentReader Documents { get; } = Substitute.For<IUserSettingsDocumentReader>();

        internal ConfigurationChangeAnnouncements Announcements { get; } =
            new(connect: null, NullLogger<ConfigurationChangeAnnouncements>.Instance);

        internal WithheldMailAccounts Withheld { get; } = new();

        internal PersistedMailSynchronizationAccounts Source { get; }

        internal void Holding(UserSettingsDocument record) =>
            this.Documents.ReadAsync(record.User, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<UserSettingsDocument?>(record));
    }
}
