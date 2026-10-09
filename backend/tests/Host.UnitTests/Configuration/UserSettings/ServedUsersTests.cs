// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Records;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Observability.ClientTelemetry;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>Covers the users this replica holds: who is read when, what keeps one current, and who the sole user is.</summary>
/// <remarks>
/// Every case reads the rows a fake store holds, and a replica that heard no announcement at all is the ordinary case:
/// a change another replica committed reaches this one through the comparison alone.
/// </remarks>
public sealed class ServedUsersTests
{
    /// <summary>The record a provisioning leaves behind, which declares nothing until its user asks for something.</summary>
    private const string EmptyRecord = "{}";

    private const string UnbindableRecord = """{"NothingBindsThis":true}""";

    private static readonly UserId Alex = SyntheticUser.Deployment;

    private static readonly UserId Morgan = UserId.Create(new Guid("4a4f1cc2-9d0e-4f1a-9b2f-6c9e2d4a7b31"));

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

    private readonly StoredUserRecords records = new();

    private readonly FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero));

    private readonly HeldBackRecords heldBack = new();

    private readonly RecordingLogger<ServedUserResolution> log = new();

    [Fact]
    public async Task ReadAsync_AUserNobodyHasAskedFor_ComposesThemFromTheirRecordAndTheAccountsAssignedToThem()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 3, WorkMailbox));
        var servedUsers = this.Cache();

        // Act
        var served = await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(served);
        Assert.Equal("alex", served.DisplayName);
        Assert.Equal([WorkMailbox.Id.ToString("D")], served.MailAccounts.Select(static account => account.AccountId));
    }

    /// <summary>A held user is answered from what was composed, which is what keeps a request from reading their record every time.</summary>
    [Fact]
    public async Task ReadAsync_AUserAlreadyHeld_AnswersWithoutReadingTheirRecordAgain()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 3));
        var servedUsers = this.Cache();

        // Act
        await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);
        var served = await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Alex, served?.User);
        Assert.Equal(1, this.records.DocumentReads);
    }

    [Fact]
    public async Task ReadAsync_AUserNoRecordHolds_ServesNothing()
    {
        // Arrange
        var servedUsers = this.Cache();

        // Act
        var served = await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(served);
    }

    /// <summary>A record that could not be read is a failure the caller sees, never a user who quietly has no mailboxes.</summary>
    [Fact]
    public async Task ReadAsync_ARecordThatCouldNotBeRead_FailsRatherThanServingNothing()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 3));
        this.records.ReadFailure = new UserSettingsUnreadableException("The user settings could not be read.");
        var servedUsers = this.Cache();

        // Act & Assert
        await Assert.ThrowsAsync<UserSettingsUnreadableException>(
            () => servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken));
    }

    /// <summary>A record this build will not bind is served from nothing and reported, rather than read again on every request.</summary>
    [Fact]
    public async Task ReadAsync_ARecordThisBuildDoesNotBind_ServesNothingAndHoldsItBack()
    {
        // Arrange
        this.records.Put(Record(Alex, UnbindableRecord, 3));
        var servedUsers = this.Cache();

        // Act
        var first = await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);
        var second = await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, this.records.DocumentReads);
        var refused = Assert.Single(this.heldBack.Current);
        Assert.Equal(HeldBackRecordKind.User, refused.Kind);
        Assert.Equal(3, refused.RejectedVersion);
    }

    /// <summary>A peek answers only from what is held, which is what lets a synchronous reader ask without a database call.</summary>
    [Fact]
    public void Peek_AUserNotHeld_AnswersNothingWithoutReadingTheirRecord()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 3));
        var servedUsers = this.Cache();

        // Act
        var served = servedUsers.Peek(Alex);

        // Assert
        Assert.Null(served);
        Assert.Equal(0, this.records.DocumentReads);
    }

    /// <summary>A record committed on another replica is recomposed here at the version the row holds, with the accounts assigned beside it.</summary>
    [Fact]
    public async Task ConvergeAsync_ARecordCommittedOnAnotherReplica_RecomposesTheHeldUserFromItsNewerVersion()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 2));
        var servedUsers = this.Cache();
        await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);
        this.records.Put(Record(Alex, EmptyRecord, 3, WorkMailbox));

        // Act
        await servedUsers.ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        var served = servedUsers.Peek(Alex);
        Assert.NotNull(served);
        Assert.Equal([WorkMailbox.Id.ToString("D")], served.MailAccounts.Select(static account => account.AccountId));
    }

    /// <summary>
    /// A held user whose row stands at the version they were composed from is not read again, which is what keeps a
    /// comparison between two changes to one statement per thousand users rather than a read of each.
    /// </summary>
    [Fact]
    public async Task ConvergeAsync_EveryHeldUserAtTheVersionTheirRowHolds_ReadsNoRecord()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 2));
        var servedUsers = this.Cache();
        await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);

        // Act
        await servedUsers.ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, this.records.DocumentReads);
    }

    /// <summary>
    /// A newer version that does not bind is not served, the version bound before it stays in force, and it is reported
    /// once rather than on every interval the same row is read at the same version.
    /// </summary>
    [Fact]
    public async Task ConvergeAsync_ANewerVersionThatDoesNotBind_KeepsServingTheLastThatBoundAndReportsItOnce()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 2, WorkMailbox));
        var servedUsers = this.Cache();
        await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);
        this.records.Put(Record(Alex, UnbindableRecord, 3, WorkMailbox));

        // Act
        await servedUsers.ConvergeAsync(TestContext.Current.CancellationToken);
        await servedUsers.ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        var served = servedUsers.Peek(Alex);
        Assert.NotNull(served);
        Assert.Equal([WorkMailbox.Id.ToString("D")], served.MailAccounts.Select(static account => account.AccountId));
        Assert.Single(this.log.Messages, message => message.Contains("at version 3", StringComparison.Ordinal));
        Assert.Equal(3, Assert.Single(this.heldBack.Current).RejectedVersion);
    }

    /// <summary>
    /// One mail account this build will not bind costs that account alone: the rest of the user's mailboxes are served
    /// at the version the row holds, so a single broken declaration never freezes a user's whole record.
    /// </summary>
    [Fact]
    public async Task ConvergeAsync_ARecordWhoseOneMailboxDoesNotBind_ServesTheRestAtTheNewVersion()
    {
        // Arrange
        var unbindable = SpareMailbox with
        {
            Document = """{"Host":"imap.example.test","NothingBindsThis":true}""",
        };
        this.records.Put(Record(Alex, EmptyRecord, 2));
        var servedUsers = this.Cache();
        await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);
        this.records.Put(Record(Alex, EmptyRecord, 3, WorkMailbox, unbindable));

        // Act
        await servedUsers.ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        var served = servedUsers.Peek(Alex);
        Assert.NotNull(served);
        Assert.Equal([WorkMailbox.Id.ToString("D")], served.MailAccounts.Select(static account => account.AccountId));
        Assert.Equal(unbindable.Id, Assert.Single(this.heldBack.Current).Identity);
    }

    /// <summary>A record repaired on another replica stops being held back here, so nobody is told to correct a row that is already correct.</summary>
    [Fact]
    public async Task ConvergeAsync_ARecordRepairedOnAnotherReplica_StopsHoldingItBack()
    {
        // Arrange
        this.records.Put(Record(Alex, UnbindableRecord, 3));
        var servedUsers = this.Cache();
        await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);
        this.records.Put(Record(Alex, EmptyRecord, 4));

        // Act
        await servedUsers.ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Alex, servedUsers.Peek(Alex)?.User);
        Assert.Empty(this.heldBack.Current);
    }

    /// <summary>A user erased on another replica is let go here, together with whatever was held back about them.</summary>
    [Fact]
    public async Task ConvergeAsync_AUserErasedOnAnotherReplica_LetsGoOfThemAndWhatWasHeldBack()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 2));
        var servedUsers = this.Cache();
        await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);
        this.heldBack.Replace(Alex, [new HeldBackRecord(HeldBackRecordKind.User, Alex.Value, "alex", 2, ["stale"])]);
        this.records.Remove(Alex);

        // Act
        await servedUsers.ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(servedUsers.Peek(Alex));
        Assert.Null(await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken));
        Assert.Empty(this.heldBack.Current);
    }

    /// <summary>A user nobody asked for within the idle lifetime is let go rather than compared, so a replica holds who is busy rather than everybody it ever served.</summary>
    [Fact]
    public async Task ConvergeAsync_AUserNobodyAskedForWithinTheIdleLifetime_IsLetGo()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 2));
        var servedUsers = this.Cache();
        await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);
        this.timeProvider.Advance(ServedUsers.IdleLifetime + TimeSpan.FromMinutes(1));

        // Act
        await servedUsers.ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(servedUsers.Peek(Alex));
    }

    /// <summary>A user let go of is compared no more, so what was held back about them goes with them rather than staying listed after the record moved.</summary>
    [Fact]
    public async Task ConvergeAsync_AnIdleUserWhoseRecordWasHeldBack_StopsHoldingItBack()
    {
        // Arrange
        this.records.Put(Record(Alex, UnbindableRecord, 3));
        var servedUsers = this.Cache();
        await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);
        this.timeProvider.Advance(ServedUsers.IdleLifetime + TimeSpan.FromMinutes(1));

        // Act
        await servedUsers.ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.heldBack.Current);
    }

    /// <summary>Every read renews a user's hold, so work that keeps asking for them is never let go of halfway.</summary>
    [Fact]
    public async Task ConvergeAsync_AUserAskedForAgainWithinTheIdleLifetime_StaysHeld()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 2));
        var servedUsers = this.Cache();
        var halfLife = ServedUsers.IdleLifetime / 2 + TimeSpan.FromMinutes(1);
        await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);
        this.timeProvider.Advance(halfLife);
        await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);
        this.timeProvider.Advance(halfLife);

        // Act
        await servedUsers.ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Alex, servedUsers.Peek(Alex)?.User);
        Assert.Equal(1, this.records.DocumentReads);
    }

    /// <summary>A slow publisher cannot put an older committed document back after a newer one reached this replica.</summary>
    [Fact]
    public void UserDocumentPublished_AnOlderCommittedVersionArrivesLast_KeepsTheNewerDocument()
    {
        // Arrange
        var servedUsers = this.Cache();
        var older = new MailSynchronizationAccountOptions { AccountId = "older" };
        var newer = new MailSynchronizationAccountOptions { AccountId = "newer" };

        // Act
        servedUsers.UserDocumentPublished(Alex, "user", new UserAccountOptions { MailAccounts = [newer] }, 3);
        servedUsers.UserDocumentPublished(Alex, "user", new UserAccountOptions { MailAccounts = [older] }, 2);

        // Assert
        Assert.Same(newer, servedUsers.Peek(Alex)?.MailAccounts.Single());
    }

    /// <summary>A committed record decides how one mailbox's mail is classified, so the held user carries the block on the account.</summary>
    /// <remarks>
    /// The whole of what makes a document actually take over: an account still answering with no block reads as
    /// classification off for that mailbox, which is not what a record stating a posture says either — a commit
    /// switching the scanner on would go on classifying nothing.
    /// </remarks>
    [Fact]
    public void UserDocumentPublished_ARecordCarryingAClassificationBlock_ServesThatAccountFromIt()
    {
        // Arrange
        var servedUsers = this.Cache();
        var classification = new MailAccountSpamClassificationOptions { Enabled = true, UseScanner = true };
        var account = new MailSynchronizationAccountOptions { AccountId = "primary", SpamClassification = classification };

        // Act
        servedUsers.UserDocumentPublished(Alex, "user", new UserAccountOptions { MailAccounts = [account] }, 2);

        // Assert
        Assert.Same(classification, servedUsers.Peek(Alex)?.MailAccounts.Single().SpamClassification);
    }

    /// <summary>A committed record decides the level this person's client is asked to record at, so the held user carries it without the process restarting.</summary>
    [Fact]
    public void UserDocumentPublished_ARecordStatingAClientTelemetryLevel_ServesThatUserFromIt()
    {
        // Arrange
        var servedUsers = this.Cache();

        // Act
        servedUsers.UserDocumentPublished(Alex, "user", new UserAccountOptions { ClientTelemetryLevel = "Debug" }, 2);

        // Assert
        Assert.Equal(ClientTelemetryLevel.Debug, servedUsers.Peek(Alex)?.ClientTelemetryLevel);
    }

    /// <summary>
    /// Reading it before the users were counted is a wiring defect rather than a deployment's problem, so it fails as
    /// one instead of answering with the identity that names nobody — which every unresolved holder would agree on.
    /// </summary>
    [Fact]
    public void User_BeforeTheUsersWereCounted_FailsRatherThanNamingNobody()
    {
        // Arrange
        var servedUsers = this.Cache();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => servedUsers.User);
    }

    [Fact]
    public async Task User_OneUserRecorded_NamesThem()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 1));
        var servedUsers = this.Cache();

        // Act
        await servedUsers.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Alex, servedUsers.User);
    }

    /// <summary>
    /// Attributing a caller to whichever user came first is how one person is handed another person's mail, so several
    /// users are answered with a classified refusal rather than a pick.
    /// </summary>
    [Fact]
    public async Task User_SeveralUsersRecorded_FailsAsAClassifiedRefusalRatherThanPickingOne()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 1));
        this.records.Put(Record(Morgan, EmptyRecord, 1));
        var servedUsers = this.Cache();

        // Act
        await servedUsers.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.Throws<DeploymentUserUnresolvedException>(() => servedUsers.User);
        Assert.Equal(MailFathomErrorCode.DeploymentUserUnresolved, refusal.ErrorCode);
    }

    /// <summary>
    /// A caller naming no user on a deployment holding nobody is told so, and what records somebody — not the sentence
    /// meant for several users, whose remedy would be a credential for a person who does not exist.
    /// </summary>
    [Fact]
    public async Task User_NobodyRecorded_NamesTheCommandThatRecordsSomebody()
    {
        // Arrange
        var servedUsers = this.Cache();

        // Act
        await servedUsers.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.Throws<DeploymentUserUnresolvedException>(() => servedUsers.User);
        Assert.Equal(MailFathomErrorCode.DeploymentUserUnresolved, refusal.ErrorCode);
        Assert.Contains("mfctl user add", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Counting asks only whether there are none, one, or several, so it reads no record however many users there are.</summary>
    [Fact]
    public async Task CountAsync_ManyUsersRecorded_ReadsNoRecordAndCountsNoFurtherThanSeveral()
    {
        // Arrange
        foreach (var user in Enumerable.Range(1, 5).Select(static index => UserId.Create(new Guid($"00000000-0000-7000-8000-00000000000{index}"))))
        {
            this.records.Put(Record(user, EmptyRecord, 1));
        }

        var servedUsers = this.Cache();

        // Act
        var held = await servedUsers.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, held);
        Assert.Equal(0, this.records.DocumentReads);
    }

    /// <summary>A user recorded on another replica counts here from the next comparison, so a sole user stops being named for a deployment that now has two.</summary>
    [Fact]
    public async Task ConvergeAsync_AUserRecordedOnAnotherReplica_StopsTheSoleUserBeingNamed()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 1));
        var servedUsers = this.Cache();
        await servedUsers.CountAsync(TestContext.Current.CancellationToken);
        this.records.Put(Record(Morgan, EmptyRecord, 1));

        // Act
        await servedUsers.ConvergeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Throws<DeploymentUserUnresolvedException>(() => servedUsers.User);
    }

    /// <summary>An erasure has to stop this replica serving the user before it deletes anything, which is what this is.</summary>
    [Fact]
    public async Task Withhold_AUserThisReplicaHolds_ServesThemNothingWhileWithheld()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 1));
        var servedUsers = this.Cache();
        await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);

        // Act
        using var withheld = servedUsers.Withhold(Alex);

        // Assert
        Assert.Null(servedUsers.Peek(Alex));
        Assert.Null(await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken));
    }

    /// <summary>A refused erasure removed nothing, so the person it was refused for goes on being served.</summary>
    [Fact]
    public async Task Withhold_DisposedWithoutTheUserBeingErased_ServesThemAgain()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 1));
        var servedUsers = this.Cache();
        await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);

        // Act
        servedUsers.Withhold(Alex).Dispose();

        // Assert
        Assert.Equal(Alex, servedUsers.Peek(Alex)?.User);
    }

    /// <summary>
    /// Two administrators erasing two people at once each withhold their own, so the erasure decided first never serves
    /// the other person again while that other deletion is still running.
    /// </summary>
    [Fact]
    public async Task Withhold_TwoUsersWithheldAtOnce_ReleasingOneKeepsTheOtherWithheld()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 1));
        this.records.Put(Record(Morgan, EmptyRecord, 1));
        var servedUsers = this.Cache();
        await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);
        await servedUsers.ReadAsync(Morgan, TestContext.Current.CancellationToken);
        using var alexWithheld = servedUsers.Withhold(Alex);

        // Act
        servedUsers.Withhold(Morgan).Dispose();

        // Assert
        Assert.True(servedUsers.IsWithheld(Alex));
        Assert.Null(servedUsers.Peek(Alex));
        Assert.Equal(Morgan, servedUsers.Peek(Morgan)?.User);
    }

    /// <summary>An erasure that committed leaves nobody to put back, whatever the withholding is disposed of after.</summary>
    [Fact]
    public async Task Withhold_TheUserWasErased_LetsGoOfThem()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 1));
        var servedUsers = this.Cache();
        await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken);

        // Act
        using (var withheld = servedUsers.Withhold(Alex))
        {
            this.records.Remove(Alex);
            withheld.Erased();
        }

        // Assert
        Assert.Null(servedUsers.Peek(Alex));
        Assert.Null(await servedUsers.ReadAsync(Alex, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A withholding happens before anything is deleted, and an erasure can still be refused. Letting it reach the
    /// sole-user reading would admit a caller naming nobody as the remaining person for the whole of the wait, the
    /// deletion, and the entire length of a refusal, where a moment earlier it had no answer at all.
    /// </summary>
    [Fact]
    public async Task User_WhileOneOfSeveralUsersIsWithheld_GoesOnRefusingToNameASoleUser()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 1));
        this.records.Put(Record(Morgan, EmptyRecord, 1));
        var servedUsers = this.Cache();
        await servedUsers.CountAsync(TestContext.Current.CancellationToken);

        // Act
        using var withheld = servedUsers.Withhold(Alex);

        // Assert
        var refusal = Assert.Throws<DeploymentUserUnresolvedException>(() => servedUsers.User);
        Assert.Equal(MailFathomErrorCode.DeploymentUserUnresolved, refusal.ErrorCode);
    }

    /// <summary>Once the deletion has committed and the users are counted again, the deployment really does serve one user, and names them.</summary>
    [Fact]
    public async Task User_AfterAWithheldUserWasErasedAndTheUsersCounted_NamesTheOneLeft()
    {
        // Arrange
        this.records.Put(Record(Alex, EmptyRecord, 1));
        this.records.Put(Record(Morgan, EmptyRecord, 1));
        var servedUsers = this.Cache();
        await servedUsers.CountAsync(TestContext.Current.CancellationToken);

        // Act
        using (var withheld = servedUsers.Withhold(Alex))
        {
            this.records.Remove(Alex);
            withheld.Erased();
        }

        await servedUsers.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Morgan, servedUsers.User);
    }

    private static UserSettingsDocument Record(UserId user, string json, long version, params MailAccountRecord[] mailAccounts) =>
        new(user, "alex", json, version) { MailAccounts = mailAccounts };

    private ServedUsers Cache() =>
        ResolvedServedUsers.Over(this.records, this.timeProvider, this.heldBack, this.log);

    /// <summary>The user rows a deployment holds, read the way the persisted reader reads them.</summary>
    private sealed class StoredUserRecords : IUserSettingsDocumentReader
    {
        private readonly Dictionary<UserId, UserSettingsDocument> rows = [];

        /// <summary>Gets how many single records were read, which is what a cache exists to keep down.</summary>
        internal int DocumentReads { get; private set; }

        /// <summary>Gets or sets what every read of one record fails with, or nothing to let it read.</summary>
        internal Exception? ReadFailure { get; set; }

        internal void Put(UserSettingsDocument document) => this.rows[document.User] = document;

        internal void Remove(UserId user) => this.rows.Remove(user);

        public Task<IReadOnlyList<UserSettingsDocumentVersion>> ReadVersionsAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserSettingsDocumentVersion>>([.. this.rows.Values.Take(limit).Select(VersionOf)]);

        public Task<IReadOnlyList<UserSettingsDocumentVersion>> ReadVersionsAsync(
            IReadOnlyCollection<UserId> users,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserSettingsDocumentVersion>>(
                [.. users.Where(this.rows.ContainsKey).Select(user => VersionOf(this.rows[user]))]);

        public Task<UserSettingsDocument?> ReadAsync(UserId user, CancellationToken cancellationToken)
        {
            if (this.ReadFailure is { } failure)
            {
                return Task.FromException<UserSettingsDocument?>(failure);
            }

            this.DocumentReads++;

            return Task.FromResult(this.rows.GetValueOrDefault(user));
        }

        private static UserSettingsDocumentVersion VersionOf(UserSettingsDocument document) =>
            new(document.User, document.Version);
    }
}
