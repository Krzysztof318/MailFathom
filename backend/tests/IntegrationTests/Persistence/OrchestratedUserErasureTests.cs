// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves that erasing a user leaves nothing behind and takes nothing of anybody else's.</summary>
/// <remarks>
/// <para>
/// This is the claim the ownership axis exists to make, and only a real database can settle it: most of the erasure is
/// PostgreSQL's own cascade, and the rest is a set of statements over tables that record a mail account with nothing
/// keying it onto one. A substitute would prove that the code intended to delete, never that a row is gone.
/// </para>
/// <para>
/// The arrangement writes one row into every table the model says records a mail account, which is why the count taken
/// before the erasure is asserted to hold no zero: a table added later and left unseeded here would otherwise make this
/// test pass while proving nothing about it. Seeding it is the work that question asks for.
/// </para>
/// <para>
/// The other half of the claim needs a second account assigned to the user this suite already has, because what
/// decides whether a mailbox is erased is the assignment relation: the erasure takes the accounts the departing user
/// was the last one assigned and leaves every other one whole. A predicate that had lost that relation would erase
/// one mailbox while answering a request about another, and a database holding only the erased user's rows could not
/// tell the two apart. That account stays in the database afterwards, like every other class's data.
/// </para>
/// <para>
/// The contact book is seeded on both sides for a reason of its own: it is the one part of a user's record that
/// records no mail account at all, so neither the count taken table by table nor the statements the seam issues itself
/// say anything about it, and it is reached only because <c>contacts</c> keys onto the user row. A book that stopped
/// being taken would leave every other assertion here green.
/// </para>
/// <para>
/// The user is provisioned by this test and erased by it, including on a failure. While it exists the deployment holds
/// two user records, which is exactly the state a configured mail account cannot be attributed in — so leaving one
/// behind would refuse the folder bindings every later class in this collection arranges.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedUserErasureTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The mailbox the erased user is the only one assigned, which is therefore erased with them.</summary>
    /// <remarks>
    /// Written in the form a deployment generates rather than as a readable name, because the mail graph keys a
    /// mailbox by the identifier the assignment relation holds as a <c>uuid</c>: an identifier of any other shape
    /// could carry no assignment, and the erasure reaches an account through exactly that relation.
    /// </remarks>
    private const string ErasedAccount = "1b8e2a40-5c31-4f7a-9d02-6ac41e5b7d10";

    /// <summary>The mailbox the surviving user is assigned, which the erasure leaves whole.</summary>
    private const string SurvivingAccount = "2c9f3b51-6d42-4a8b-8e13-7bd52f6c8e21";

    /// <summary>What the caller states it is holding stopped, which for the erased user is their one mailbox.</summary>
    /// <remarks>
    /// The walk refuses an account it is about to delete and which is not stated here, so passing it is part of
    /// arranging an erasure rather than a formality: a test that seeded a second mailbox for this user and left it out
    /// would be refused, which is the guarantee that behaviour exists for.
    /// </remarks>
    private static readonly Guid[] QuiescedAccounts = [Guid.Parse(ErasedAccount)];

    private const string AccountIdentifierPropertyName = nameof(MailFolderEntity.MailboxAccountId);

    /// <summary>The one address in the erased user's book, in a domain no other class here writes into.</summary>
    private const string ErasedUserContactAddress = "correspondent@user-erasure.contacts.test";

    /// <summary>The one address this test adds to the surviving user's book, which stays behind with the rest of it.</summary>
    private const string SurvivingUserContactAddress = "correspondent@user-erasure-bystander.contacts.test";

    // The comparison form the domain derives and the column therefore holds, stated once so a row is sought by what a
    // deployment would have written rather than by the form a literal happens to be typed in.
    private const string ErasedUserContactNormalizedAddress = "CORRESPONDENT@USER-ERASURE.CONTACTS.TEST";

    private const string SurvivingUserContactNormalizedAddress =
        "CORRESPONDENT@USER-ERASURE-BYSTANDER.CONTACTS.TEST";

    [Fact]
    public async Task EraseAsync_TwoUsersWithAMailboxEach_LeavesNoRowOfOnesAndEveryRowOfTheOthers()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var erasedUserId = Guid.CreateVersion7();
        var survivingUserId = await ReadSoleUserAsync(services, cancellationToken);

        try
        {
            var storedEmailId = await services.CommitProducingAsync(
                (_, session, token) => SeedUserAsync(session, erasedUserId, token),
                cancellationToken);

            Assert.Equal(
                PersistenceCommitResult.Committed,
                await services.CommitAsync(
                    (_, session, token) => SeedBystanderAsync(session, survivingUserId, token),
                    cancellationToken));

            var rowsBefore = await CountRowsNamingAccountAsync(services, ErasedAccount, cancellationToken);
            var bystanderRowsBefore = await CountRowsNamingAccountAsync(services, SurvivingAccount, cancellationToken);

            // Act
            var erasure = await services.CommitProducingAsync(
                (_, session, token) => UserAccountErasure.EraseAsync(session, erasedUserId, QuiescedAccounts, token),
                cancellationToken);

            // Assert
            Assert.DoesNotContain(0L, rowsBefore.Values);
            Assert.True(erasure.UserErased);

            // Positive rather than an exact number: what the seam owes is that the rows no cascade reaches are gone,
            // which the count taken afterwards states table by table. A number here would only restate how many tables
            // the walk names, which is the unit test's claim.
            Assert.True(erasure.RowsErasedBesideTheCascade > 0);

            var rowsAfter = await CountRowsNamingAccountAsync(services, ErasedAccount, cancellationToken);
            Assert.DoesNotContain(rowsAfter, table => table.Value != 0);

            // The other user's account, table by table and count by count. The tables are named rather than counted so
            // that the equality below is read against a stated arrangement: three of them are reached only through the
            // cascade and three only through the statements the seam issues itself, which is where a predicate that had
            // lost the user would take somebody else's rows.
            var bystanderRowsAfter = await CountRowsNamingAccountAsync(services, SurvivingAccount, cancellationToken);

            string[] tablesHoldingTheBystander =
                [.. bystanderRowsBefore.Where(table => table.Value != 0).Select(table => table.Key)];

            Assert.Equal(
                [
                    "email_threads",
                    "mail_drafts",
                    "mail_folders",
                    "mail_rederivation_positions",
                    "mailbox_refresh_tokens",
                    "stored_emails",
                ],
                tablesHoldingTheBystander);
            Assert.Equal(bystanderRowsBefore, bystanderRowsAfter);

            await services.InScopeAsync(
                async (scope, token) =>
                {
                    var context = scope.GetRequiredService<MailFathomDbContext>();

                    // The two ends of the cascade the account column never reaches: the raw MIME of the erased mail,
                    // which hangs off the message, and the citation, which hangs off an audit entry the seam took.
                    Assert.Equal(
                        0,
                        await context.EmailMessageContents
                            .CountAsync(content => content.StoredEmailId == storedEmailId, token));
                    Assert.Equal(
                        0,
                        await context.Set<MailAnsweringAuditedEmailEntity>()
                            .CountAsync(citation => citation.StoredEmailId == storedEmailId, token));

                    // The contact book, which the cascade reaches through the user rather than through an account, so
                    // neither the counts above nor a statement of the seam's own says anything about it: the person and
                    // the address row that hung off them are both gone.
                    Assert.Equal(
                        0,
                        await context.Contacts.CountAsync(contact => contact.UserId == erasedUserId, token));
                    Assert.Equal(
                        0,
                        await context.ContactAddresses
                            .CountAsync(
                                address => address.NormalizedAddress == ErasedUserContactNormalizedAddress,
                                token));

                    // Nobody else's: the user this suite's own mail hangs off is still there, and the counts above say
                    // their account is too, down to the raw MIME of the message stored beneath it.
                    Assert.True(await context.UserAccounts.AnyAsync(user => user.Id == survivingUserId, token));
                    Assert.Equal(
                        1,
                        await context.ContactAddresses
                            .CountAsync(
                                address => address.NormalizedAddress == SurvivingUserContactNormalizedAddress,
                                token));
                    Assert.Equal(
                        1,
                        await context.EmailMessageContents
                            .CountAsync(content => content.StoredEmail.MailboxAccountId == SurvivingAccount, token));

                    // The two tables the cascade reaches through a draft rather than through an account. Neither names
                    // one, so the counts above say nothing about them: the erased user's staged file and its octets
                    // are gone with the draft, and the other user's are still where they were put.
                    Assert.Equal(
                        0,
                        await context.Set<MailDraftAttachmentEntity>()
                            .CountAsync(attachment => attachment.MailDraft.UserId == erasedUserId, token));
                    Assert.Equal(
                        0,
                        await context.Set<MailDraftAttachmentContentEntity>()
                            .CountAsync(octets => octets.Attachment.MailDraft.UserId == erasedUserId, token));
                    Assert.Equal(
                        1,
                        await context.Set<MailDraftAttachmentEntity>()
                            .CountAsync(attachment => attachment.MailDraft.UserId == survivingUserId, token));
                    Assert.Equal(
                        1,
                        await context.Set<MailDraftAttachmentContentEntity>()
                            .CountAsync(octets => octets.Attachment.MailDraft.UserId == survivingUserId, token));

                    return 0;
                },
                cancellationToken);
        }
        finally
        {
            // Through the seam rather than by hand, so a test that failed part-way still leaves the deployment with the
            // one user record every folder binding after it is resolved against.
            await services.CommitProducingAsync(
                (_, session, token) => UserAccountErasure.EraseAsync(session, erasedUserId, QuiescedAccounts, token),
                CancellationToken.None);
        }
    }

    /// <summary>
    /// The claim the quiescing exists to make, and only a real database can settle it: the exclusion is the lease
    /// table's, so what stops an erasure running over a synchronization pass is a row another replica wrote rather
    /// than anything either process knows about the other. A refusal has to leave every row where it was — half an
    /// erasure is worse than none — and the same request has to go through once the pass has ended.
    /// </summary>
    [Fact]
    public async Task RunQuiescedAsync_AccountSupervisedElsewhere_ErasesNothingUntilThatSupervisionEnds()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var erasedUserId = Guid.CreateVersion7();
        var account = MailAccountId.Create(ErasedAccount);
        var quiescing = Quiescing(services);

        try
        {
            await services.CommitProducingAsync(
                (_, session, token) => SeedUserAsync(session, erasedUserId, token),
                cancellationToken);

            var rowsBefore = await CountRowsNamingAccountAsync(services, ErasedAccount, cancellationToken);

            // Another replica's supervisor, as the coordinator takes it: the same scope, from a service graph of its
            // own over the one database.
            await using var otherReplica = await OrchestratedMailFathomServices.StartAsync(
                orchestration,
                cancellationToken);
            var supervising = await TakeSupervisionAsync(otherReplica, account, cancellationToken);

            // The arrangement is the claim's whole premise, so a scope this replica was refused says so here rather
            // than as a failure further down.
            Assert.NotNull(supervising);

            string? refusal;
            IReadOnlyDictionary<string, long> rowsWhileSupervised;

            // Act
            try
            {
                refusal = await quiescing.RunQuiescedAsync(
                    [account],
                    token => EraseAsync(services, erasedUserId, token),
                    cancellationToken);

                rowsWhileSupervised = await CountRowsNamingAccountAsync(services, ErasedAccount, cancellationToken);
            }
            finally
            {
                // A ten-minute lease on this account is left behind otherwise, and the class's next test would then be
                // refused on supervision rather than on the job claim it is about.
                await supervising.ReleaseAsync();
                supervising.Dispose();
            }

            var refusalAfterTheRunEnded = await quiescing.RunQuiescedAsync(
                [account],
                token => EraseAsync(services, erasedUserId, token),
                cancellationToken);

            // Assert
            Assert.DoesNotContain(0L, rowsBefore.Values);
            Assert.NotNull(refusal);
            Assert.Contains(ErasedAccount, refusal, StringComparison.Ordinal);

            // Nothing at all was deleted while the account was still being written to.
            Assert.Equal(rowsBefore, rowsWhileSupervised);

            Assert.Null(refusalAfterTheRunEnded);

            var rowsAfter = await CountRowsNamingAccountAsync(services, ErasedAccount, cancellationToken);
            Assert.DoesNotContain(rowsAfter, table => table.Value != 0);
        }
        finally
        {
            await services.CommitProducingAsync(
                (_, session, token) => UserAccountErasure.EraseAsync(session, erasedUserId, QuiescedAccounts, token),
                CancellationToken.None);
        }
    }

    /// <summary>
    /// A job is claimed by type and reads no lease of the caller's, so the supervision hold reaches none of it. Two
    /// separate statements answer that — the wait's read in front of the transaction, and the transaction's own lock
    /// over the account's claimable rows — and both are raw SQL over a real queue, so a wrong column or a state literal
    /// that matches nothing would fail open in every unit test and be found here or nowhere.
    /// </summary>
    [Fact]
    public async Task EraseAsync_AJobClaimedForTheAccount_ErasesNothingUntilThatClaimEnds()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var erasedUserId = Guid.CreateVersion7();
        var account = MailAccountId.Create(ErasedAccount);
        var quiescing = Quiescing(services);

        try
        {
            await services.CommitProducingAsync(
                (_, session, token) => SeedUserAsync(session, erasedUserId, token),
                cancellationToken);

            // The seeded row arrives pending, which is a job nobody is running; a claim is what makes it work in
            // flight, and what the erasure has to refuse over.
            await SetSeededJobStateAsync(services, JobState.Claimed, cancellationToken);

            var rowsBefore = await CountRowsNamingAccountAsync(services, ErasedAccount, cancellationToken);

            // Act
            var refusal = await quiescing.RunQuiescedAsync(
                [account],
                token => EraseAsync(services, erasedUserId, token),
                cancellationToken);

            var rowsWhileClaimed = await CountRowsNamingAccountAsync(services, ErasedAccount, cancellationToken);

            // The transaction refuses on its own as well, which is what covers a claim landing after the wait above
            // has already answered empty.
            var withoutTheWait = await EraseAsync(services, erasedUserId, cancellationToken);
            var rowsAfterTheTransactionRefused =
                await CountRowsNamingAccountAsync(services, ErasedAccount, cancellationToken);

            await SetSeededJobStateAsync(services, JobState.Succeeded, cancellationToken);

            var refusalAfterTheJobFinished = await quiescing.RunQuiescedAsync(
                [account],
                token => EraseAsync(services, erasedUserId, token),
                cancellationToken);

            // Assert
            Assert.DoesNotContain(0L, rowsBefore.Values);
            Assert.NotNull(refusal);
            Assert.Contains(ErasedAccount, refusal, StringComparison.Ordinal);
            Assert.Equal(rowsBefore, rowsWhileClaimed);

            Assert.False(withoutTheWait.UserErased);
            Assert.Equal(Guid.Parse(ErasedAccount), withoutTheWait.UnquiescedAccount);
            Assert.Equal(rowsBefore, rowsAfterTheTransactionRefused);

            // A finished claim is not work in flight, so the same request goes through and takes everything.
            Assert.Null(refusalAfterTheJobFinished);

            var rowsAfter = await CountRowsNamingAccountAsync(services, ErasedAccount, cancellationToken);
            Assert.DoesNotContain(rowsAfter, table => table.Value != 0);
        }
        finally
        {
            await services.CommitProducingAsync(
                (_, session, token) => UserAccountErasure.EraseAsync(session, erasedUserId, QuiescedAccounts, token),
                CancellationToken.None);
        }
    }

    /// <summary>Composes the quiescing with a bound this test states rather than waits out.</summary>
    /// <remarks>
    /// Zero, because what is asserted is which answer each state produces and never how long the wait before it took.
    /// A bound a test sat through would prove the same thing half a minute later and put the suite's own clock in the
    /// claim.
    /// </remarks>
    private static MailAccountWorkQuiesce Quiescing(OrchestratedMailFathomServices services) => new(
        services.ScopeFactory,
        new FixedMailSynchronizationSettings(),
        NullLoggerFactory.Instance,
        TimeProvider.System,
        TimeSpan.Zero);

    private static Task<WorkLeaseHold?> TakeSupervisionAsync(
        OrchestratedMailFathomServices replica,
        MailAccountId account,
        CancellationToken cancellationToken) => WorkLeaseHold.TryTakeAsync(
        MailAccountSupervisionScope.For(account),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(5),
        replica.ScopeFactory,
        NullLogger<WorkLeaseHold>.Instance,
        TimeProvider.System,
        cancellationToken);

    private static Task<UserErasure> EraseAsync(
        OrchestratedMailFathomServices services,
        Guid userId,
        CancellationToken cancellationToken) => services.CommitProducingAsync(
        (_, session, token) => UserAccountErasure.EraseAsync(session, userId, QuiescedAccounts, token),
        cancellationToken);

    /// <summary>Moves the seeded account's job into one state, which is how this class states what the queue is doing.</summary>
    private static Task<int> SetSeededJobStateAsync(
        OrchestratedMailFathomServices services,
        JobState state,
        CancellationToken cancellationToken) => services.InScopeAsync(
        (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
            .Jobs
            .Where(job => job.MailboxAccountId == ErasedAccount)
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.State, state), token),
        cancellationToken);

    private static Task<Guid> ReadSoleUserAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .UserAccounts
                .AsNoTracking()
                .Select(user => user.Id)
                .SingleAsync(token),
            cancellationToken);

    /// <summary>Writes one user, one mailbox, and a row in every table that records a mail account.</summary>
    /// <returns>The identity of the stored message, which the derived rows beneath it are read back by.</returns>
    private static async Task<Guid> SeedUserAsync(
        IPersistenceSession session,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var now = DateTimeOffset.UnixEpoch;

        context.UserAccounts.Add(new UserAccountEntity
        {
            Id = userId,
            DisplayName = $"user-{userId:N}",
            Document = "{}",
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var account = new MailboxAccountEntity { Id = ErasedAccount, };

        AssignAccountTo(context, ErasedAccount, userId, now);
        var folder = new MailFolderEntity
        {
            MailboxAccountId = account.Id,
            MailboxAccount = account,
            Alias = "inbox",
            RemotePath = "INBOX",
        };
        var thread = new EmailThreadEntity
        {
            Id = Guid.CreateVersion7(),
            MailboxAccountId = account.Id,
            AssembledAt = now,
        };
        var storedEmail = new StoredEmailEntity
        {
            Id = Guid.CreateVersion7(),
            MailboxAccountId = account.Id,
            MailFolder = folder,
            UidValidity = 1,
            Uid = 1,
            Subject = "user erasure",
            SizeOctets = RepresentativeRawMime.Length,
            ContentAvailability = StoredEmailContentAvailability.Available,
            EmailThreadId = thread.Id,
            StoredAt = now,
        };
        var outgoingEmail = new OutgoingEmailEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            MailboxAccountId = account.Id,
            RequesterIdentity = "user-erasure",
            MimeByteLength = RepresentativeRawMime.Length,
            RecordedAt = now,
            StageChangedAt = now,
            AvailableAt = now,
        };
        var auditEntry = new MailAnsweringAuditEntryEntity
        {
            Id = Guid.CreateVersion7(),
            RunId = Guid.CreateVersion7(),
            MailboxAccountId = account.Id,
            ChatEndpointAlias = "primary",
            InstructionsVersion = "user-erasure",
            StartedAt = now,
            CompletedAt = now,
            Outcome = "Answered",
            Degradation = "None",
        };

        context.MailboxAccounts.Add(account);
        context.MailFolders.Add(folder);
        context.EmailThreads.Add(thread);
        context.StoredEmails.Add(storedEmail);
        context.OutgoingEmails.Add(outgoingEmail);
        context.MailAnsweringAuditEntries.Add(auditEntry);

        context.EmailMessageContents.Add(new EmailMessageContentEntity
        {
            StoredEmailId = storedEmail.Id,
            StoredEmail = storedEmail,
            RawMime = RepresentativeRawMime,
            MimeByteLength = RepresentativeRawMime.Length,
            Sha256Hash = new byte[32],
            StoredAt = now,
        });
        context.EmailThreadIdentifiers.Add(new EmailThreadIdentifierEntity
        {
            MailboxAccountId = account.Id,
            IdentifierHash = new string('a', 64),
            EmailThreadId = thread.Id,
        });
        context.MailboxMutations.Add(new MailboxMutationEntity
        {
            Id = Guid.CreateVersion7(),
            StoredEmailId = storedEmail.Id,
            StoredEmail = storedEmail,
            MailboxAccountId = account.Id,
            MailFolder = folder,
            UidValidity = 1,
            Uid = 1,
            Mutation = "SetSeen",
            RequesterIdentity = "user-erasure",
            RecordedAt = now,
            StageChangedAt = now,
        });
        context.MailRuleExecutions.Add(new MailRuleExecutionEntity
        {
            Id = Guid.CreateVersion7(),
            MailboxAccountId = account.Id,
            StoredEmailId = storedEmail.Id,
            RuleName = "user-erasure",
            Revision = "rev000000000",
            Trigger = "Arrival",
            Outcome = "Matched",
            ReadFacts = ["senderDomain"],
            EvaluatedAt = now,
            Duration = TimeSpan.FromMilliseconds(1),
        });
        context.Jobs.Add(new JobEntity
        {
            Id = Guid.CreateVersion7(),
            JobType = "UserErasure",
            IdempotencyKey = $"user-erasure-{account.Id}",
            Payload = "{}",
            MailboxAccountId = account.Id,
            AvailableAt = now,
            TurnAt = now,
            EnqueuedAt = now,
            StateChangedAt = now,
        });
        context.Set<MailAnsweringAuditedEmailEntity>().Add(new MailAnsweringAuditedEmailEntity
        {
            MailAnsweringAuditEntryId = auditEntry.Id,
            StoredEmailId = storedEmail.Id,
            Position = 0,
            WasCited = true,
        });
        context.Set<OutgoingEmailFilingEntity>().Add(new OutgoingEmailFilingEntity
        {
            OutgoingEmailId = outgoingEmail.Id,
            OutgoingEmail = outgoingEmail,
            Filing = "SentCopy",
            MailboxAccountId = account.Id,
            FolderAlias = "sent",
            FolderPath = "Sent",
            AppendedAt = now,
        });
        var draft = new MailDraftEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            MailboxAccountId = account.Id,
            RequesterIdentity = "user-erasure",
            Subject = string.Empty,
            MimeByteLength = RepresentativeRawMime.Length,
            ComposedAt = now,
            RevisedAt = now,
        };
        context.MailDrafts.Add(draft);
        StageAFileOn(context, draft, now);
        context.RecurringSends.Add(new RecurringSendEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            MailboxAccountId = account.Id,
            RequesterIdentity = "user-erasure",
            Schedule = "0 9 * * 1",
            DraftByteLength = RepresentativeRawMime.Length,
            DeclaredAt = now,
        });
        context.MailboxMutationAuditEntries.Add(new MailboxMutationAuditEntryEntity
        {
            Id = Guid.CreateVersion7(),
            MutationRecordId = Guid.CreateVersion7(),
            MailboxAccountId = account.Id,
            StoredEmailId = storedEmail.Id,
            Mutation = "SetSeen",
            SourceFolderPath = "INBOX",
            SourceUidValidity = 1,
            SourceUid = 1,
            RequesterIdentity = "user-erasure",
            RequestedAt = now,
            CompletedAt = now,
        });
        context.MailboxRefreshTokens.Add(new MailboxRefreshTokenEntity
        {
            MailboxAccountId = account.Id,
            SealedRefreshToken = [1, 2, 3, 4],
            DataEncryptionKeyId = OrchestratedMailFathomServices.DataEncryptionKeyId,
            UpdatedAt = now,
        });
        context.MailRederivationPositions.Add(new MailRederivationPositionEntity
        {
            MailboxAccountId = account.Id,
            FolderAlias = "inbox",
            LastProcessedStoredEmailId = storedEmail.Id,
            UpdatedAt = now,
        });
        context.MailRederivationRuns.Add(new MailRederivationRunEntity
        {
            MailboxAccountId = account.Id,
            FolderAlias = "inbox",
            RunId = Guid.CreateVersion7(),
            RequestedAt = now,
        });
        context.MailRuleEvaluationRuns.Add(new MailRuleEvaluationRunEntity
        {
            MailboxAccountId = account.Id,
            RequestedAt = now,
        });
        context.SpamClassificationRuns.Add(new SpamClassificationRunEntity
        {
            MailboxAccountId = account.Id,
            RequestedAt = now,
            FolderAliases = ["inbox"],
        });

        // The contact book records no mail account, so no statement of the seam's names it and the counts above never
        // see it. It hangs off the user directly, which is the whole of what takes it.
        AddContact(context, userId, "Erased Correspondent", ErasedUserContactAddress);

        await context.SaveChangesAsync(cancellationToken);

        return storedEmail.Id;
    }

    /// <summary>Records one mail account and assigns it to one user, which is what decides whether an erasure takes it.</summary>
    /// <remarks>
    /// Both rows, because the assignment keys onto the account's record rather than onto the mail graph's own row:
    /// the record is the account as an administrator holds it, and the graph beneath it is keyed by the same
    /// identifier written as text. Seeding only the graph would leave a mailbox no user is assigned, which the
    /// erasure reads as nobody's rather than as this user's.
    /// </remarks>
    private static void AssignAccountTo(
        MailFathomDbContext context,
        string accountId,
        Guid userId,
        DateTimeOffset assignedAt)
    {
        var id = Guid.Parse(accountId);

        context.MailAccountRecords.Add(new MailAccountRecordEntity
        {
            Id = id,
            DisplayName = $"account-{id:N}",
            Document = "{}",
            Version = 1,
            CreatedAt = assignedAt,
            UpdatedAt = assignedAt,
        });
        context.MailAccountAssignments.Add(new MailAccountAssignmentEntity
        {
            UserId = userId,
            MailAccountId = id,
            AssignedAt = assignedAt,
        });
    }

    /// <summary>Writes one person into a user's book, with the one address row that hangs off them.</summary>
    /// <summary>Stages one file and its octets against a draft, which is what a cascade two tables deep is read from.</summary>
    /// <remarks>
    /// Neither table names an account, so nothing the erasure seam issues itself reaches them: the file hangs off the
    /// draft and the octets hang off the file, and both go only because PostgreSQL takes them with their parent. That
    /// is the claim, and a foreign key declared without a cascading action would leave a person's staged files behind
    /// after their erasure while every count the seam takes still read zero.
    /// </remarks>
    private static void StageAFileOn(MailFathomDbContext context, MailDraftEntity draft, DateTimeOffset stagedAt)
    {
        var attachment = new MailDraftAttachmentEntity
        {
            Id = Guid.CreateVersion7(stagedAt),
            MailDraftId = draft.Id,
            MailDraft = draft,
            FileName = "report.pdf",
            MediaType = "application/pdf",
            ByteLength = RepresentativeRawMime.Length,
            StagedAt = stagedAt,
        };

        context.Set<MailDraftAttachmentEntity>().Add(attachment);
        context.Set<MailDraftAttachmentContentEntity>().Add(new MailDraftAttachmentContentEntity
        {
            MailDraftAttachmentId = attachment.Id,
            Attachment = attachment,
            Content = RepresentativeRawMime,
        });
    }

    private static void AddContact(MailFathomDbContext context, Guid userId, string displayName, string address)
    {
        var contact = new ContactEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            DisplayName = displayName,
            DisplayNameSortKey = displayName.ToUpperInvariant(),
            PreferredNormalizedAddress = address.ToUpperInvariant(),
            Origin = ContactOrigin.Asserted,
            RecordedAt = DateTimeOffset.UnixEpoch,
            AmendedAt = DateTimeOffset.UnixEpoch,
        };

        context.Contacts.Add(contact);
        context.ContactAddresses.Add(new ContactAddressEntity
        {
            Id = Guid.CreateVersion7(),
            ContactId = contact.Id,
            UserId = userId,
            Address = address,
            NormalizedAddress = address.ToUpperInvariant(),
        });
    }

    /// <summary>Writes a second account under the user who is not being erased, with mail and rows beneath it.</summary>
    /// <remarks>
    /// Deliberately spread across both halves of the erasure: the folder, the thread, the message, and its raw MIME are
    /// what the cascade would reach through a user, and the draft, the refresh token, and the re-derivation cursor are
    /// three of the tables no cascade reaches and that the seam therefore takes with statements of its own. A predicate
    /// that lost the user would show up in the second group first, which is why the group is the one seeded thickest.
    /// </remarks>
    private static async Task SeedBystanderAsync(
        IPersistenceSession session,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var now = DateTimeOffset.UnixEpoch;

        var account = new MailboxAccountEntity { Id = SurvivingAccount, };

        AssignAccountTo(context, SurvivingAccount, userId, now);
        var folder = new MailFolderEntity
        {
            MailboxAccountId = account.Id,
            MailboxAccount = account,
            Alias = "inbox",
            RemotePath = "INBOX",
        };
        var thread = new EmailThreadEntity
        {
            Id = Guid.CreateVersion7(),
            MailboxAccountId = account.Id,
            AssembledAt = now,
        };
        var storedEmail = new StoredEmailEntity
        {
            Id = Guid.CreateVersion7(),
            MailboxAccountId = account.Id,
            MailFolder = folder,
            UidValidity = 1,
            Uid = 1,
            Subject = "user erasure bystander",
            SizeOctets = RepresentativeRawMime.Length,
            ContentAvailability = StoredEmailContentAvailability.Available,
            EmailThreadId = thread.Id,
            StoredAt = now,
        };

        context.MailboxAccounts.Add(account);
        context.MailFolders.Add(folder);
        context.EmailThreads.Add(thread);
        context.StoredEmails.Add(storedEmail);

        context.EmailMessageContents.Add(new EmailMessageContentEntity
        {
            StoredEmailId = storedEmail.Id,
            StoredEmail = storedEmail,
            RawMime = RepresentativeRawMime,
            MimeByteLength = RepresentativeRawMime.Length,
            Sha256Hash = new byte[32],
            StoredAt = now,
        });
        var draft = new MailDraftEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            MailboxAccountId = account.Id,
            RequesterIdentity = "user-erasure-bystander",
            Subject = string.Empty,
            MimeByteLength = RepresentativeRawMime.Length,
            ComposedAt = now,
            RevisedAt = now,
        };
        context.MailDrafts.Add(draft);
        StageAFileOn(context, draft, now);
        context.MailboxRefreshTokens.Add(new MailboxRefreshTokenEntity
        {
            MailboxAccountId = account.Id,
            SealedRefreshToken = [5, 6, 7, 8],
            DataEncryptionKeyId = OrchestratedMailFathomServices.DataEncryptionKeyId,
            UpdatedAt = now,
        });
        context.MailRederivationPositions.Add(new MailRederivationPositionEntity
        {
            MailboxAccountId = account.Id,
            FolderAlias = "inbox",
            LastProcessedStoredEmailId = storedEmail.Id,
            UpdatedAt = now,
        });

        AddContact(context, userId, "Bystander Correspondent", SurvivingUserContactAddress);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Counts, per table, the rows that name one account.</summary>
    /// <remarks>
    /// The tables come from the model rather than from a list here, so the assertion covers every one of them and a
    /// table added later is counted without this class being edited — which is what makes the zero check before the
    /// erasure the question it is meant to be.
    /// </remarks>
    private static Task<IReadOnlyDictionary<string, long>> CountRowsNamingAccountAsync(
        OrchestratedMailFathomServices services,
        string accountId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();
                var counts = new Dictionary<string, long>(StringComparer.Ordinal);

                var accountTables = context.Model.GetEntityTypes()
                    .Where(entityType => entityType.FindProperty(AccountIdentifierPropertyName) is not null)
                    .OrderBy(entityType => entityType.GetTableName(), StringComparer.Ordinal);

                foreach (var entityType in accountTables)
                {
                    var table = entityType.GetTableName()!;
                    var column = entityType.FindProperty(AccountIdentifierPropertyName)!.GetColumnName();

                    // The account is a parameter; the two identifiers are the model's own names for a table and a
                    // column, which is the only thing PostgreSQL accepts no parameter in the position of.
                    var statement =
                        $$"""SELECT count(*) AS "Value" FROM "{{table}}" WHERE "{{column}}" = {0}""";

                    var rows = await context.Database
                        .SqlQueryRaw<long>(statement, accountId)
                        .ToListAsync(token);

                    counts[table] = rows.Single();
                }

                return (IReadOnlyDictionary<string, long>)counts;
            },
            cancellationToken);

    private static byte[] RepresentativeRawMime =>
        "From: sender@mailfathom.test\r\nSubject: user erasure\r\n\r\nBody.\r\n"u8.ToArray();

    /// <summary>The shipped synchronization defaults, which is all the quiescing reads: how long a hold lasts and how often it is renewed.</summary>
    /// <remarks>
    /// Written here rather than resolved, because the orchestrated graph composes no published snapshot of this
    /// section — nothing in the suite runs the coordinator that would read one — and the two durations are the
    /// deployment's own defaults either way.
    /// </remarks>
    private sealed class FixedMailSynchronizationSettings : ISettingsSnapshot<MailSynchronizationOptions>
    {
        public MailSynchronizationOptions Current { get; } = new();

        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);
    }
}
