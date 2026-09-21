// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Folders.Local;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the two phases a held account's copy is made of against the real schema and the real content store.</summary>
/// <remarks>
/// <para>
/// Three claims no substitute settles. That the copy is a second row with a payload of its own needs a real content
/// store to show two distinct objects rather than two references to one — which is what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see>
/// refuses to share. That the copy's row is written, filed, and pointed at that payload in one transaction needs the
/// real cascade of foreign keys the schema declares. And that the per-account stored-content ledger counts the copy is
/// a figure a maintained counter moves inside the same transaction, which nothing in the unit suite can observe.
/// </para>
/// <para>
/// The ledger is shared by every class in the collection, so its movement is measured against what it held just before
/// the commit rather than as an absolute; the collection runs one test at a time, which is what makes the movement
/// this class's alone.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedLocalMailCopyTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "local-mail-copy";

    private const uint CopiedUid = 820;

    /// <summary>Past the payload PostgreSQL keeps in a heap page, so the ledger moves by a length no rounding could produce.</summary>
    private const int PayloadByteCount = 64 * 1024;

    /// <summary>The whole act in one: a second row, a second payload, filed where it was copied to, and counted.</summary>
    [Fact]
    public async Task CommitAsync_ACopyOnAHeldAccount_WritesASecondRowWithItsOwnPayloadAndCountsIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            storesContentInObjectStorage: true);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var rawMime = SyntheticEmail.RawMimeOf("local-mail-copy-source", PayloadByteCount);
        var source = await StoreAsync(
            services,
            SyntheticEmail.OccurrenceIn(binding, CopiedUid),
            rawMime,
            cancellationToken);
        var folder = await CommitFolderAsync(services, cancellationToken);
        var sourceLocator = await ReadObjectLocatorAsync(services, source, cancellationToken);
        var storedBytesBefore = await ReadStoredContentBytesAsync(services, cancellationToken);
        var flags = new CopiedMailFlags(IsSeen: true, IsFlagged: true, RemoteEmailKeywords.Create(["$Label1"]));

        // Act
        var prepared = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<LocalMailCopier>()
                .PrepareAsync(SyntheticMailAccount.Account, source, token),
            cancellationToken);
        var copy = await services.CommitProducingAsync(
            (scope, session, token) => scope.GetRequiredService<LocalMailCopier>()
                .CommitAsync(session, prepared!, binding.Id, folder, flags, token),
            cancellationToken);

        // Assert
        Assert.NotNull(prepared);
        Assert.NotNull(copy);
        Assert.NotEqual(source, copy);

        var copyLocator = await ReadObjectLocatorAsync(services, copy.Value, cancellationToken);
        Assert.NotEqual(sourceLocator, copyLocator);

        var written = await ReadCopyAsync(services, copy.Value, cancellationToken);
        Assert.Equal(folder.Value, written.LocalMailFolderId);
        Assert.Null(written.UidValidity);
        Assert.Null(written.Uid);
        Assert.True(written.IsRemotelySeen);
        Assert.True(written.IsRemotelyFlagged);
        Assert.Equal(flags.Keywords.Values, written.RemoteKeywords);

        // Stamped so a rule that copies into a folder it also matches on does not meet its own copy.
        Assert.NotNull(written.RulesEvaluatedAt);

        Assert.Equal(
            storedBytesBefore + rawMime.LongLength,
            await ReadStoredContentBytesAsync(services, cancellationToken));
    }

    /// <summary>Stores one occurrence's metadata and its raw MIME the way synchronization does: placed first, staged second.</summary>
    private static async Task<StoredEmailId> StoreAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
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
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, "local-mail-copy-source", rawMime.Length),
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

    /// <summary>Creates the local folder the copy is filed into, which a held account keeps for its own hierarchy.</summary>
    private static async Task<LocalMailFolderId> CommitFolderAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var folder = new LocalMailFolder(
            LocalMailFolderId.Create(Guid.CreateVersion7()),
            ParentId: null,
            LocalMailFolderName.Create("Copies"),
            Role: null,
            SourceFolderAlias: null);

        await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<ILocalMailFolderStore>().SaveAsync(
                session,
                SyntheticMailAccount.Account,
                [folder],
                [],
                token),
            cancellationToken);

        return folder.Id;
    }

    private static Task<long> ReadStoredContentBytesAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IAccountStoredContentLedger>()
                .ReadStoredContentBytesAsync(SyntheticMailAccount.Account, token),
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

    private static Task<CopiedRow> ReadCopyAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AsNoTracking()
                .Where(email => email.Id == storedEmailId.Value)
                .Select(email => new CopiedRow(
                    email.LocalMailFolderId,
                    email.UidValidity,
                    email.Uid,
                    email.IsRemotelySeen,
                    email.IsRemotelyFlagged,
                    email.RemoteKeywords,
                    email.RulesEvaluatedAt))
                .SingleAsync(token),
            cancellationToken);

    /// <summary>The columns the copy is judged by, projected rather than tracked so nothing loads the payload.</summary>
    private sealed record CopiedRow(
        Guid? LocalMailFolderId,
        uint? UidValidity,
        uint? Uid,
        bool IsRemotelySeen,
        bool IsRemotelyFlagged,
        string[] RemoteKeywords,
        DateTimeOffset? RulesEvaluatedAt);
}
