// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.ObjectStorage;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the stored state a held account's change commits to is read, written, and erased against the real schema.</summary>
/// <remarks>
/// <para>
/// Every claim here is one only a real database settles. The erasure is a raw ledger statement issued before a removal
/// the payload leaves with through PostgreSQL's cascade, and the object it frees is deleted only once the commit is
/// durable; the account scoping is a translated predicate over two columns; and the source folder a read answers with is
/// rebuilt from a binding row whose delimiter PostgreSQL stores as text.
/// </para>
/// <para>
/// The ledger is shared by every class in the collection, so the erasure is measured as a movement from what the figure
/// held just before it rather than as an absolute; the collection runs one test at a time, which is what makes that
/// movement this class's alone.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedLocalEmailStateStoreTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "local-email-state";

    private const uint ErasedUid = 810;

    private const uint ForeignAccountUid = 811;

    private const uint RoundTrippedUid = 812;

    /// <summary>Past the payload PostgreSQL keeps in a heap page, so the figure moves by a length no rounding could produce.</summary>
    private const int PayloadByteCount = 64 * 1024;

    /// <summary>
    /// The whole cascade a held account's delete runs, in one: the row is gone, the object its payload was held in is
    /// gone from the endpoint, and the user's stored-content figure gave back exactly the bytes the payload recorded.
    /// </summary>
    [Fact]
    public async Task EraseAsync_AnObjectBackedMessage_RemovesTheRowReleasesTheObjectAndGivesItsBytesBack()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            storesContentInObjectStorage: true);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, ErasedUid);
        var rawMime = SyntheticEmail.RawMimeOf("local-email-state-erased", PayloadByteCount);
        var storedEmailId = await StoreAsync(services, occurrenceId, "local-email-state-erased", rawMime, cancellationToken);

        var objectLocator = await ReadObjectLocatorAsync(services, storedEmailId, cancellationToken);
        var objectLengthBefore = await ObjectEndpointProbe.ReadObjectLengthAsync(services, objectLocator, cancellationToken);
        var rowsBefore = await CountStoredEmailRowsAsync(services, storedEmailId, cancellationToken);
        var storedBytesBefore = await ReadStoredContentBytesAsync(services, cancellationToken);

        // Act
        var commitResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<ILocalEmailStateStore>().EraseAsync(
                session,
                SyntheticMailAccount.Account,
                storedEmailId,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        // The same observations report the row and the object present before the erasure, so their absence afterwards
        // is the erasure's doing rather than a probe that finds nothing.
        Assert.Equal(1, rowsBefore);
        Assert.Equal(rawMime.LongLength, objectLengthBefore);

        Assert.Equal(0, await CountStoredEmailRowsAsync(services, storedEmailId, cancellationToken));
        Assert.Equal(0, await CountContentRowsAsync(services, storedEmailId, cancellationToken));
        Assert.Null(await ObjectEndpointProbe.ReadObjectLengthAsync(services, objectLocator, cancellationToken));
        Assert.Equal(
            storedBytesBefore - rawMime.LongLength,
            await ReadStoredContentBytesAsync(services, cancellationToken));
    }

    /// <summary>
    /// A message is reachable only through the account that holds it: a write naming it under another account is refused
    /// and leaves the row as it was, and a read under another account or another user answers as absent.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ForAnAccountThatDoesNotHoldTheMessage_IsRefusedAndTheMessageReadsAsAbsentThere()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var storedEmailId = await StoredSyntheticEmail.MetadataOnlyAsync(
            services,
            SyntheticEmail.OccurrenceIn(binding, ForeignAccountUid),
            "local-email-state-foreign",
            cancellationToken);

        var anotherAccount = MailAccountIdentity.Create(
            SyntheticMailAccount.User,
            MailAccountId.Create("local-email-state-elsewhere"));
        var anotherUser = MailAccountIdentity.Create(
            MailUserId.Create(Guid.CreateVersion7()),
            SyntheticMailAccount.AccountId);
        var refusedState = new LocalEmailState(
            binding,
            Folder: null,
            IsSeen: true,
            IsFlagged: true,
            RemoteEmailKeywords.Create(["foreign"]));

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<ILocalEmailStateStore>().WriteAsync(
                session,
                anotherAccount,
                storedEmailId,
                refusedState,
                token),
            cancellationToken));

        var readUnderAnotherAccount = await ReadAsync(services, anotherAccount, storedEmailId, cancellationToken);
        var readUnderAnotherUser = await ReadAsync(services, anotherUser, storedEmailId, cancellationToken);
        var readUnderItsOwnAccount = await ReadAsync(services, SyntheticMailAccount.Account, storedEmailId, cancellationToken);

        // Assert
        Assert.Null(readUnderAnotherAccount);
        Assert.Null(readUnderAnotherUser);

        Assert.NotNull(readUnderItsOwnAccount);
        Assert.False(readUnderItsOwnAccount.IsSeen);
        Assert.False(readUnderItsOwnAccount.IsFlagged);
        Assert.Equal(RemoteEmailKeywords.None, readUnderItsOwnAccount.Keywords);
    }

    /// <summary>
    /// What a change writes is what the next read in another transaction answers with, and the source folder that read
    /// rebuilds carries the hierarchy delimiter the binding row stored.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ThenReadAsync_AnswersTheFlagsKeywordsAndDelimitedSourceFolderWritten()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var storedEmailId = await StoredSyntheticEmail.MetadataOnlyAsync(
            services,
            SyntheticEmail.OccurrenceIn(binding, RoundTrippedUid),
            "local-email-state-round-trip",
            cancellationToken);
        var written = new LocalEmailState(
            binding,
            Folder: null,
            IsSeen: true,
            IsFlagged: true,
            RemoteEmailKeywords.Create(["$Label1", "project"]));

        // Act
        var commitResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<ILocalEmailStateStore>().WriteAsync(
                session,
                SyntheticMailAccount.Account,
                storedEmailId,
                written,
                token),
            cancellationToken);
        var readBack = await ReadAsync(services, SyntheticMailAccount.Account, storedEmailId, cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
        Assert.NotNull(readBack);
        Assert.Equal(binding, readBack.SourceFolder);
        Assert.Equal('.', readBack.SourceFolder.RemotePath.HierarchyDelimiter);
        Assert.Null(readBack.Folder);
        Assert.True(readBack.IsSeen);
        Assert.True(readBack.IsFlagged);
        Assert.Equal(written.Keywords, readBack.Keywords);
    }

    /// <summary>Reads through the store inside a session of its own, which is the only way the port reads.</summary>
    private static Task<LocalEmailState?> ReadAsync(
        OrchestratedMailFathomServices services,
        MailAccountIdentity account,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.CommitProducingAsync(
            (scope, session, token) => scope.GetRequiredService<ILocalEmailStateStore>().ReadAsync(
                session,
                account,
                storedEmailId,
                token),
            cancellationToken);

    /// <summary>Stores one occurrence's metadata and its raw MIME the way synchronization does: placed first, staged second.</summary>
    private static async Task<StoredEmailId> StoreAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        string subject,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        var placement = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmailContentStore>().PlaceContentAsync(
                EmailContentKind.IncomingMessage,
                rawMime,
                token),
            cancellationToken);

        return await services.CommitProducingAsync(
            async (scope, session, token) =>
            {
                var storedEmailId = await scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                    session, SyntheticMailAccount.User,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, subject, rawMime.Length),
                    extractedMetadata: null,
                    StoredEmailContentAvailability.Available,
                    token);

                await scope.GetRequiredService<IEmailContentStore>().SaveContentAsync(
                    session,
                    storedEmailId,
                    occurrenceId,
                    placement,
                    token);

                return storedEmailId;
            },
            cancellationToken);
    }

    private static Task<long> ReadStoredContentBytesAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IUserStoredContentLedger>()
                .ReadStoredContentBytesAsync(SyntheticMailAccount.User, token),
            cancellationToken);

    private static Task<string> ReadObjectLocatorAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .EmailMessageContents
                .AsNoTracking()
                .Where(content => content.StoredEmailId == storedEmailId.Value)
                .Select(content => content.ObjectLocator!)
                .SingleAsync(token),
            cancellationToken);

    private static Task<int> CountStoredEmailRowsAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AsNoTracking()
                .CountAsync(email => email.Id == storedEmailId.Value, token),
            cancellationToken);

    private static Task<int> CountContentRowsAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .EmailMessageContents
                .AsNoTracking()
                .CountAsync(content => content.StoredEmailId == storedEmailId.Value, token),
            cancellationToken);
}
