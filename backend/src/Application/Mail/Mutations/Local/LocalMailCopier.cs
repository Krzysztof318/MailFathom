// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Folders.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Mutations.Local;

/// <summary>Writes the second stored message a copy on a held account produces.</summary>
/// <remarks>
/// <para>
/// A copy is the one local act that creates mail rather than changing it, so it is the one that cannot be a single
/// write inside the caller's transaction:
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0008-copied-message-local-identity.md">ADR 0008</see>
/// makes the copy a second stored message with a payload of its own, and
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see>
/// refuses to share one payload between two rows, while a payload has to be placed with no transaction open across the
/// placement. The act is therefore two-phase, exactly as filing a draft or a sent copy is: <see cref="PrepareAsync" />
/// reads the message and places its payload before the transaction, and <see cref="CommitAsync" /> writes the row,
/// points it at the placed payload, and files it, inside the transaction the change commits in.
/// </para>
/// <para>
/// Nothing is carried across from the source row but the flags and the keywords the caller supplies. The metadata, the
/// search document, and the conversation are derived again from the payload, which is what ADR 0008 says a copy is: two
/// local messages that happen to share their content, each deriving its own.
/// </para>
/// <para>
/// A copy placed and never committed leaves an object nothing points at. That is the direction the two phases are
/// ordered in deliberately — the reverse would be a row naming a payload that was never placed — and the content
/// reclamation pass takes such an object exactly as it takes one a rolled-back transaction left.
/// </para>
/// </remarks>
public sealed class LocalMailCopier
{
    private readonly IEmailMetadataRepository emails;
    private readonly IEmailContentStore contents;
    private readonly IEmailMimeReader mimeReader;
    private readonly ILocalMailFolderStore folders;

    /// <summary>Initializes the copier over the stores a copied message is written through.</summary>
    /// <param name="emails">Writes the copy's own stored message.</param>
    /// <param name="contents">Reads the copied message's payload, places the copy's, and saves it.</param>
    /// <param name="mimeReader">Reads the metadata the copy is searched and threaded by.</param>
    /// <param name="folders">Files the copy into the local folder it was copied into.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public LocalMailCopier(
        IEmailMetadataRepository emails,
        IEmailContentStore contents,
        IEmailMimeReader mimeReader,
        ILocalMailFolderStore folders)
    {
        ArgumentNullException.ThrowIfNull(emails);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(mimeReader);
        ArgumentNullException.ThrowIfNull(folders);

        this.emails = emails;
        this.contents = contents;
        this.mimeReader = mimeReader;
        this.folders = folders;
    }

    /// <summary>Reads a message and places the payload its copy will carry, before the transaction that commits the copy.</summary>
    /// <param name="account">The account the message is stored for.</param>
    /// <param name="source">The message being copied.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The prepared copy, or <see langword="null" /> where the account stores no payload for the message.</returns>
    /// <remarks>
    /// A message whose payload is not stored — one a storage ceiling deferred, or one whose content a repair has found
    /// unreadable — cannot be copied, because there is nothing to place. It is answered rather than raised: the copy is
    /// one action of a batch, and the rest of the batch is still to be applied.
    /// </remarks>
    public async Task<PreparedLocalCopy?> PrepareAsync(
        MailAccountId account,
        StoredEmailId source,
        CancellationToken cancellationToken)
    {
        if (await this.contents.FindStoredContentAsync(source, cancellationToken) is not { } stored)
        {
            return null;
        }

        var extraction = await this.mimeReader.ReadMetadataAsync(account, stored.RawMime, cancellationToken);
        var placed = await this.contents.PlaceContentAsync(
            EmailContentKind.IncomingMessage,
            stored.RawMime,
            cancellationToken);

        return new PreparedLocalCopy(account, source, extraction.Metadata, placed);
    }

    /// <summary>Writes a prepared copy as a stored message of its own, inside the transaction the copy commits in.</summary>
    /// <param name="session">The transaction the copy commits in.</param>
    /// <param name="copy">The prepared copy.</param>
    /// <param name="binding">The folder binding the copy is stored under, which is the copied message's own.</param>
    /// <param name="folder">The local folder the copy is filed into.</param>
    /// <param name="flags">The flags and keywords the copied message carries, which the copy takes.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The copy's identity, or <see langword="null" /> where the binding it was prepared against is gone.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="copy" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The copy takes the copied message's binding rather than the destination's, for the reason a local move leaves the
    /// binding alone: no local act moves an occurrence, and the binding is where the source folder held the message
    /// rather than where MailFathom keeps it. The copy holds no occurrence at all, because no server placed it.
    /// </remarks>
    public async Task<StoredEmailId?> CommitAsync(
        IPersistenceSession session,
        PreparedLocalCopy copy,
        MailFolderResolutionId binding,
        LocalMailFolderId folder,
        CopiedMailFlags flags,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(copy);

        if (await this.emails.StoreLocalCopyAsync(
                session,
                copy.Account,
                binding,
                copy.Metadata,
                copy.Content.ByteLength,
                flags,
                cancellationToken) is not { } email)
        {
            return null;
        }

        await this.contents.SaveContentAsync(session, email, occurrenceId: null, copy.Content, cancellationToken);
        await this.folders.PlaceAsync(session, copy.Account, email, folder, cancellationToken);

        return email;
    }
}
