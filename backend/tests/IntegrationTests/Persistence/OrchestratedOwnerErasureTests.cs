// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Owners;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves that erasing an owner leaves nothing behind and takes nothing of anybody else's.</summary>
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
/// The other half of the claim needs a second account under the owner this suite already has, because every statement
/// the seam issues itself is bounded by a subquery rather than by the cascade — a predicate written against the account
/// instead of the owner would erase one owner's mail while answering a request about another's, and a database holding
/// only the erased owner's rows could not tell the two apart. That account stays in the database afterwards, like every
/// other class's data: it belongs to the owner this suite already had, and nothing resolves an owner from an account.
/// </para>
/// <para>
/// The contact book is seeded on both sides for a reason of its own: it is the one part of an owner's record that
/// records no mail account at all, so neither the count taken table by table nor the statements the seam issues itself
/// say anything about it, and it is reached only because <c>contacts</c> keys onto the owner row. A book that stopped
/// being taken would leave every other assertion here green.
/// </para>
/// <para>
/// The owner is provisioned by this test and erased by it, including on a failure. While it exists the deployment holds
/// two owner records, which is exactly the state a configured mail account cannot be attributed in — so leaving one
/// behind would refuse the folder bindings every later class in this collection arranges.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOwnerErasureTests(MailFathomOrchestrationFixture orchestration)
{
    private const string ErasedAccount = "owner-erasure-account";

    private const string SurvivingAccount = "owner-erasure-bystander";

    private const string AccountIdentifierPropertyName = nameof(MailFolderEntity.MailboxAccountId);

    /// <summary>The one address in the erased owner's book, in a domain no other class here writes into.</summary>
    private const string ErasedOwnerContactAddress = "correspondent@owner-erasure.contacts.test";

    /// <summary>The one address this test adds to the surviving owner's book, which stays behind with the rest of it.</summary>
    private const string SurvivingOwnerContactAddress = "correspondent@owner-erasure-bystander.contacts.test";

    // The comparison form the domain derives and the column therefore holds, stated once so a row is sought by what a
    // deployment would have written rather than by the form a literal happens to be typed in.
    private const string ErasedOwnerContactNormalizedAddress = "CORRESPONDENT@OWNER-ERASURE.CONTACTS.TEST";

    private const string SurvivingOwnerContactNormalizedAddress =
        "CORRESPONDENT@OWNER-ERASURE-BYSTANDER.CONTACTS.TEST";

    [Fact]
    public async Task EraseAsync_TwoOwnersWithAMailboxEach_LeavesNoRowOfOnesAndEveryRowOfTheOthers()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var erasedOwnerId = Guid.CreateVersion7();
        var survivingOwnerId = await ReadSoleOwnerAsync(services, cancellationToken);

        try
        {
            var storedEmailId = await services.CommitProducingAsync(
                (_, session, token) => SeedOwnerAsync(session, erasedOwnerId, token),
                cancellationToken);

            Assert.Equal(
                PersistenceCommitResult.Committed,
                await services.CommitAsync(
                    (_, session, token) => SeedBystanderAsync(session, survivingOwnerId, token),
                    cancellationToken));

            var rowsBefore = await CountRowsNamingAccountAsync(services, ErasedAccount, cancellationToken);
            var bystanderRowsBefore = await CountRowsNamingAccountAsync(services, SurvivingAccount, cancellationToken);

            // Act
            var erasure = await services.CommitProducingAsync(
                (_, session, token) => OwnerAccountErasure.EraseAsync(session, erasedOwnerId, token),
                cancellationToken);

            // Assert
            Assert.DoesNotContain(0L, rowsBefore.Values);
            Assert.True(erasure.OwnerErased);

            // Positive rather than an exact number: what the seam owes is that the rows no cascade reaches are gone,
            // which the count taken afterwards states table by table. A number here would only restate how many tables
            // the walk names, which is the unit test's claim.
            Assert.True(erasure.RowsErasedBesideTheCascade > 0);

            var rowsAfter = await CountRowsNamingAccountAsync(services, ErasedAccount, cancellationToken);
            Assert.DoesNotContain(rowsAfter, table => table.Value != 0);

            // The other owner's account, table by table and count by count. The tables are named rather than counted so
            // that the equality below is read against a stated arrangement: three of them are reached only through the
            // cascade and three only through the statements the seam issues itself, which is where a predicate that had
            // lost the owner would take somebody else's rows.
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

                    // The contact book, which the cascade reaches through the owner rather than through an account, so
                    // neither the counts above nor a statement of the seam's own says anything about it: the person and
                    // the address row that hung off them are both gone.
                    Assert.Equal(
                        0,
                        await context.Contacts.CountAsync(contact => contact.OwnerId == erasedOwnerId, token));
                    Assert.Equal(
                        0,
                        await context.ContactAddresses
                            .CountAsync(
                                address => address.NormalizedAddress == ErasedOwnerContactNormalizedAddress,
                                token));

                    // Nobody else's: the owner this suite's own mail hangs off is still there, and the counts above say
                    // their account is too, down to the raw MIME of the message stored beneath it.
                    Assert.True(await context.OwnerAccounts.AnyAsync(owner => owner.Id == survivingOwnerId, token));
                    Assert.Equal(
                        1,
                        await context.ContactAddresses
                            .CountAsync(
                                address => address.NormalizedAddress == SurvivingOwnerContactNormalizedAddress,
                                token));
                    Assert.Equal(
                        1,
                        await context.EmailMessageContents
                            .CountAsync(content => content.StoredEmail.MailboxAccountId == SurvivingAccount, token));

                    // The two tables the cascade reaches through a draft rather than through an account. Neither names
                    // one, so the counts above say nothing about them: the erased owner's staged file and its octets
                    // are gone with the draft, and the other owner's are still where they were put.
                    Assert.Equal(
                        0,
                        await context.Set<MailDraftAttachmentEntity>()
                            .CountAsync(attachment => attachment.MailDraft.OwnerId == erasedOwnerId, token));
                    Assert.Equal(
                        0,
                        await context.Set<MailDraftAttachmentContentEntity>()
                            .CountAsync(octets => octets.Attachment.MailDraft.OwnerId == erasedOwnerId, token));
                    Assert.Equal(
                        1,
                        await context.Set<MailDraftAttachmentEntity>()
                            .CountAsync(attachment => attachment.MailDraft.OwnerId == survivingOwnerId, token));
                    Assert.Equal(
                        1,
                        await context.Set<MailDraftAttachmentContentEntity>()
                            .CountAsync(octets => octets.Attachment.MailDraft.OwnerId == survivingOwnerId, token));

                    return 0;
                },
                cancellationToken);
        }
        finally
        {
            // Through the seam rather than by hand, so a test that failed part-way still leaves the deployment with the
            // one owner record every folder binding after it is resolved against.
            await services.CommitProducingAsync(
                (_, session, token) => OwnerAccountErasure.EraseAsync(session, erasedOwnerId, token),
                CancellationToken.None);
        }
    }

    private static Task<Guid> ReadSoleOwnerAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .OwnerAccounts
                .AsNoTracking()
                .Select(owner => owner.Id)
                .SingleAsync(token),
            cancellationToken);

    /// <summary>Writes one owner, one mailbox, and a row in every table that records a mail account.</summary>
    /// <returns>The identity of the stored message, which the derived rows beneath it are read back by.</returns>
    private static async Task<Guid> SeedOwnerAsync(
        IPersistenceSession session,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var now = DateTimeOffset.UnixEpoch;

        context.OwnerAccounts.Add(new OwnerAccountEntity
        {
            Id = ownerId,
            DisplayName = $"owner-{ownerId:N}",
            Document = "{}",
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var account = new MailboxAccountEntity { Id = ErasedAccount, OwnerId = ownerId };
        var folder = new MailFolderEntity
        {
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            MailboxAccount = account,
            Alias = "inbox",
            RemotePath = "INBOX",
        };
        var thread = new EmailThreadEntity
        {
            Id = Guid.CreateVersion7(),
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            AssembledAt = now,
        };
        var storedEmail = new StoredEmailEntity
        {
            Id = Guid.CreateVersion7(),
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            MailFolder = folder,
            UidValidity = 1,
            Uid = 1,
            Subject = "owner erasure",
            SizeOctets = RepresentativeRawMime.Length,
            ContentAvailability = StoredEmailContentAvailability.Available,
            EmailThreadId = thread.Id,
            StoredAt = now,
        };
        var outgoingEmail = new OutgoingEmailEntity
        {
            Id = Guid.CreateVersion7(),
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            RequesterIdentity = "owner-erasure",
            MimeByteLength = RepresentativeRawMime.Length,
            RecordedAt = now,
            StageChangedAt = now,
            AvailableAt = now,
        };
        var auditEntry = new MailAnsweringAuditEntryEntity
        {
            Id = Guid.CreateVersion7(),
            RunId = Guid.CreateVersion7(),
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            ChatEndpointAlias = "primary",
            InstructionsVersion = "owner-erasure",
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
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            IdentifierHash = new string('a', 64),
            EmailThreadId = thread.Id,
        });
        context.MailboxMutations.Add(new MailboxMutationEntity
        {
            Id = Guid.CreateVersion7(),
            StoredEmailId = storedEmail.Id,
            StoredEmail = storedEmail,
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            MailFolder = folder,
            UidValidity = 1,
            Uid = 1,
            Mutation = "SetSeen",
            RequesterIdentity = "owner-erasure",
            RecordedAt = now,
            StageChangedAt = now,
        });
        context.MailRuleExecutions.Add(new MailRuleExecutionEntity
        {
            Id = Guid.CreateVersion7(),
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            StoredEmailId = storedEmail.Id,
            RuleName = "owner-erasure",
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
            JobType = "OwnerErasure",
            IdempotencyKey = $"owner-erasure-{account.Id}",
            Payload = "{}",
            OwnerId = account.OwnerId,
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
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            FolderAlias = "sent",
            FolderPath = "Sent",
            AppendedAt = now,
        });
        var draft = new MailDraftEntity
        {
            Id = Guid.CreateVersion7(),
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            RequesterIdentity = "owner-erasure",
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
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            RequesterIdentity = "owner-erasure",
            Schedule = "0 9 * * 1",
            DraftByteLength = RepresentativeRawMime.Length,
            DeclaredAt = now,
        });
        context.MailboxMutationAuditEntries.Add(new MailboxMutationAuditEntryEntity
        {
            Id = Guid.CreateVersion7(),
            MutationRecordId = Guid.CreateVersion7(),
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            StoredEmailId = storedEmail.Id,
            Mutation = "SetSeen",
            SourceFolderPath = "INBOX",
            SourceUidValidity = 1,
            SourceUid = 1,
            RequesterIdentity = "owner-erasure",
            RequestedAt = now,
            CompletedAt = now,
        });
        context.MailboxRefreshTokens.Add(new MailboxRefreshTokenEntity
        {
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            SealedRefreshToken = [1, 2, 3, 4],
            DataEncryptionKeyId = OrchestratedMailFathomServices.DataEncryptionKeyId,
            UpdatedAt = now,
        });
        context.MailRederivationPositions.Add(new MailRederivationPositionEntity
        {
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            FolderAlias = "inbox",
            LastProcessedStoredEmailId = storedEmail.Id,
            UpdatedAt = now,
        });
        context.MailRederivationRuns.Add(new MailRederivationRunEntity
        {
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            FolderAlias = "inbox",
            RunId = Guid.CreateVersion7(),
            RequestedAt = now,
        });
        context.MailRuleEvaluationRuns.Add(new MailRuleEvaluationRunEntity
        {
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            RequestedAt = now,
        });
        context.SpamClassificationRuns.Add(new SpamClassificationRunEntity
        {
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            RequestedAt = now,
            FolderAliases = ["inbox"],
        });

        // The contact book records no mail account, so no statement of the seam's names it and the counts above never
        // see it. It hangs off the owner directly, which is the whole of what takes it.
        AddContact(context, ownerId, "Erased Correspondent", ErasedOwnerContactAddress);

        await context.SaveChangesAsync(cancellationToken);

        return storedEmail.Id;
    }

    /// <summary>Writes one person into an owner's book, with the one address row that hangs off them.</summary>
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

    private static void AddContact(MailFathomDbContext context, Guid ownerId, string displayName, string address)
    {
        var contact = new ContactEntity
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
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
            OwnerId = ownerId,
            Address = address,
            NormalizedAddress = address.ToUpperInvariant(),
        });
    }

    /// <summary>Writes a second account under the owner who is not being erased, with mail and rows beneath it.</summary>
    /// <remarks>
    /// Deliberately spread across both halves of the erasure: the folder, the thread, the message, and its raw MIME are
    /// what the cascade would reach through an owner, and the draft, the refresh token, and the re-derivation cursor are
    /// three of the tables no cascade reaches and that the seam therefore takes with statements of its own. A predicate
    /// that lost the owner would show up in the second group first, which is why the group is the one seeded thickest.
    /// </remarks>
    private static async Task SeedBystanderAsync(
        IPersistenceSession session,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var now = DateTimeOffset.UnixEpoch;

        var account = new MailboxAccountEntity { Id = SurvivingAccount, OwnerId = ownerId };
        var folder = new MailFolderEntity
        {
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            MailboxAccount = account,
            Alias = "inbox",
            RemotePath = "INBOX",
        };
        var thread = new EmailThreadEntity
        {
            Id = Guid.CreateVersion7(),
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            AssembledAt = now,
        };
        var storedEmail = new StoredEmailEntity
        {
            Id = Guid.CreateVersion7(),
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            MailFolder = folder,
            UidValidity = 1,
            Uid = 1,
            Subject = "owner erasure bystander",
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
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            RequesterIdentity = "owner-erasure-bystander",
            Subject = string.Empty,
            MimeByteLength = RepresentativeRawMime.Length,
            ComposedAt = now,
            RevisedAt = now,
        };
        context.MailDrafts.Add(draft);
        StageAFileOn(context, draft, now);
        context.MailboxRefreshTokens.Add(new MailboxRefreshTokenEntity
        {
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            SealedRefreshToken = [5, 6, 7, 8],
            DataEncryptionKeyId = OrchestratedMailFathomServices.DataEncryptionKeyId,
            UpdatedAt = now,
        });
        context.MailRederivationPositions.Add(new MailRederivationPositionEntity
        {
            OwnerId = account.OwnerId,
            MailboxAccountId = account.Id,
            FolderAlias = "inbox",
            LastProcessedStoredEmailId = storedEmail.Id,
            UpdatedAt = now,
        });

        AddContact(context, ownerId, "Bystander Correspondent", SurvivingOwnerContactAddress);

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
        "From: sender@mailfathom.test\r\nSubject: owner erasure\r\n\r\nBody.\r\n"u8.ToArray();
}
