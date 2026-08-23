// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
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
/// The owner is provisioned by this test and erased by it, including on a failure. While it exists the deployment holds
/// two owner records, which is exactly the state a configured mail account cannot be attributed in — so leaving one
/// behind would refuse the folder bindings every later class in this collection arranges.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOwnerErasureTests(MailFathomOrchestrationFixture orchestration)
{
    private const string ErasedAccount = "owner-erasure-account";

    private const string AccountIdentifierPropertyName = nameof(MailFolderEntity.MailboxAccountId);

    [Fact]
    public async Task EraseAsync_AnOwnerWithAMailboxBeneathThem_LeavesNoRowNamingTheirAccount()
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

            var rowsBefore = await CountRowsNamingTheErasedAccountAsync(services, cancellationToken);

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

            var rowsAfter = await CountRowsNamingTheErasedAccountAsync(services, cancellationToken);
            Assert.DoesNotContain(rowsAfter, table => table.Value != 0);

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

                    // Nobody else's. The owner this suite's own mail hangs off is still there, and so is their mail.
                    Assert.True(await context.OwnerAccounts.AnyAsync(owner => owner.Id == survivingOwnerId, token));

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
        var context = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var now = DateTimeOffset.UnixEpoch;

        context.OwnerAccounts.Add(new OwnerAccountEntity
        {
            Id = ownerId,
            Document = "{}",
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var account = new MailboxAccountEntity { Id = ErasedAccount, OwnerId = ownerId };
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
            Subject = "owner erasure",
            SizeOctets = RepresentativeRawMime.Length,
            ContentAvailability = StoredEmailContentAvailability.Available,
            EmailThreadId = thread.Id,
            StoredAt = now,
        };
        var outgoingEmail = new OutgoingEmailEntity
        {
            Id = Guid.CreateVersion7(),
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
            RequesterIdentity = "owner-erasure",
            RecordedAt = now,
            StageChangedAt = now,
        });
        context.MailRuleExecutions.Add(new MailRuleExecutionEntity
        {
            Id = Guid.CreateVersion7(),
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
            MailboxAccountId = account.Id,
            AvailableAt = now,
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
        context.MailDrafts.Add(new MailDraftEntity
        {
            Id = Guid.CreateVersion7(),
            MailboxAccountId = account.Id,
            RequesterIdentity = "owner-erasure",
            MimeByteLength = RepresentativeRawMime.Length,
            ComposedAt = now,
            RevisedAt = now,
        });
        context.RecurringSends.Add(new RecurringSendEntity
        {
            Id = Guid.CreateVersion7(),
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

        await context.SaveChangesAsync(cancellationToken);

        return storedEmail.Id;
    }

    /// <summary>Counts, per table, the rows that name the erased account.</summary>
    /// <remarks>
    /// The tables come from the model rather than from a list here, so the assertion covers every one of them and a
    /// table added later is counted without this class being edited — which is what makes the zero check before the
    /// erasure the question it is meant to be.
    /// </remarks>
    private static Task<IReadOnlyDictionary<string, long>> CountRowsNamingTheErasedAccountAsync(
        OrchestratedMailFathomServices services,
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
                        .SqlQueryRaw<long>(statement, ErasedAccount)
                        .ToListAsync(token);

                    counts[table] = rows.Single();
                }

                return (IReadOnlyDictionary<string, long>)counts;
            },
            cancellationToken);

    private static byte[] RepresentativeRawMime =>
        "From: sender@mailfathom.test\r\nSubject: owner erasure\r\n\r\nBody.\r\n"u8.ToArray();
}
