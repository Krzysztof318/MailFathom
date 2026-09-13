// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Folders;
using MailFathom.Application.Folders.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>Files a draft or a sent copy into the local folder of an account whose mailbox MailFathom holds alone.</summary>
/// <remarks>
/// <para>
/// It is the held account's counterpart of <see cref="MailboxCopyAppender" />, and the reason it exists is the same one
/// that makes appending wrong there: the source is drained, so a copy appended to it would be drained straight back and
/// would make filing wait on IMAP. What is written instead is a stored message in the local folder playing the role, in
/// the transaction that settles the draft revision or the delivery the message is a copy of — so there is no moment at
/// which one is committed and the other is not, and nothing to resume between them.
/// </para>
/// <para>
/// No IMAP command is issued by anything here, and nothing here decides whether an account is held on the strength of
/// what it read before the transaction: <see cref="FileAsync" /> reads the account's phase again inside it and files
/// nothing where the account has stopped being held.
/// </para>
/// </remarks>
public sealed class LocalMailFiler
{
    private readonly ILocalMailFolderStore folders;
    private readonly IEmailMetadataRepository emails;
    private readonly IEmailContentStore contents;
    private readonly IEmailMimeReader mimeReader;
    private readonly IMailFolderMappingReader folderMappings;
    private readonly IMailFolderResolutionStore folderResolutions;
    private readonly IOutgoingMailFilingPolicyReader filingPolicies;
    private readonly IOutgoingMailFilingStore filings;
    private readonly ClientSignals signals;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the filer from the stores a filed message is written through.</summary>
    /// <param name="folders">Reads the account's phase and folders, and places and erases what is filed.</param>
    /// <param name="emails">Writes the stored message.</param>
    /// <param name="contents">Places and saves the message's payload, and reads a send's.</param>
    /// <param name="mimeReader">Reads the metadata the stored message is searched and threaded by.</param>
    /// <param name="folderMappings">Finds the source folder playing the role a message is filed under.</param>
    /// <param name="folderResolutions">Finds that folder's current binding.</param>
    /// <param name="filingPolicies">Answers whether an account files a sent copy at all.</param>
    /// <param name="filings">Records why a sent copy could not be prepared.</param>
    /// <param name="signals">Tells the account's clients what was filed, once it commits.</param>
    /// <param name="timeProvider">Supplies the instant a supplied folder's identity is minted at.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public LocalMailFiler(
        ILocalMailFolderStore folders,
        IEmailMetadataRepository emails,
        IEmailContentStore contents,
        IEmailMimeReader mimeReader,
        IMailFolderMappingReader folderMappings,
        IMailFolderResolutionStore folderResolutions,
        IOutgoingMailFilingPolicyReader filingPolicies,
        IOutgoingMailFilingStore filings,
        ClientSignals signals,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(emails);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(mimeReader);
        ArgumentNullException.ThrowIfNull(folderMappings);
        ArgumentNullException.ThrowIfNull(folderResolutions);
        ArgumentNullException.ThrowIfNull(filingPolicies);
        ArgumentNullException.ThrowIfNull(filings);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.folders = folders;
        this.emails = emails;
        this.contents = contents;
        this.mimeReader = mimeReader;
        this.folderMappings = folderMappings;
        this.folderResolutions = folderResolutions;
        this.filingPolicies = filingPolicies;
        this.filings = filings;
        this.signals = signals;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reports whether MailFathom holds the account's mailbox alone, which is when its copies are filed here.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the account is held.</returns>
    public async Task<bool> HoldsAsync(MailAccountIdentity account, CancellationToken cancellationToken) =>
        await this.folders.ReadAsync(account, cancellationToken) is { Phase: MailAccountCustodyPhase.Held };

    /// <summary>Reads and places a message for filing, before the transaction that files it.</summary>
    /// <param name="account">The account the message is filed for.</param>
    /// <param name="filing">Which place it is filed into.</param>
    /// <param name="rawMime">The message.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The prepared copy, or <see langword="null" /> where no bound source folder of the account plays the role.</returns>
    public async Task<LocalMailCopy?> PrepareAsync(
        MailAccountIdentity account,
        OutgoingMailFiling filing,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        if (this.folderMappings.FindFolderPlayingRole(account.Id, filing.Role) is not { } mapping
            || await this.folderResolutions.GetCurrentResolutionAsync(account, mapping.Alias, cancellationToken)
                is not { } binding)
        {
            return null;
        }

        var extraction = await this.mimeReader.ReadMetadataAsync(account, rawMime, cancellationToken);
        var placed = await this.contents.PlaceContentAsync(EmailContentKind.IncomingMessage, rawMime, cancellationToken);

        return new LocalMailCopy(account, filing, binding.Id, extraction.Metadata, placed);
    }

    /// <summary>Prepares the local copy of a draft revision written before its account's copy could be filed.</summary>
    /// <param name="draft">The draft, whose current revision's message is read back from the content store.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The prepared copy, or <see langword="null" /> where no message is stored for the draft or no bound folder plays the drafts role.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="draft" /> is <see langword="null" />.</exception>
    public async Task<LocalMailCopy?> PrepareDraftAsync(MailDraftRecord draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return await this.contents.FindMailDraftContentAsync(draft.Id, cancellationToken) is { } content
            ? await this.PrepareAsync(draft.Account, OutgoingMailFiling.Draft, content.RawMime, cancellationToken)
            : null;
    }

    /// <summary>Prepares the sent copy of a delivered message, where its account is held and files one.</summary>
    /// <param name="record">The send that was accepted.</param>
    /// <returns>The prepared copy, or <see langword="null" /> where none is to be filed or none could be prepared.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Nothing escapes it, because it runs between a submission server accepting the message and the record saying so,
    /// and a failure that stopped that write would leave a delivered message looking undelivered. A copy that could not be
    /// prepared is recorded as the filing failure it is and the delivery commits without it.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The delivery this copy belongs to has already reached its recipients, and recording it must not wait on a copy; every failure is recorded against the send's filing instead.")]
    public async Task<LocalMailCopy?> PrepareSentCopyAsync(OutgoingEmailRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!this.filingPolicies.FilesSentCopy(record.AccountId))
        {
            return null;
        }

        try
        {
            if (!await this.HoldsAsync(record.Account, CancellationToken.None)
                || await this.contents.FindOutgoingContentAsync(record.Id, CancellationToken.None) is not { } content)
            {
                return null;
            }

            if (await this.PrepareAsync(record.Account, OutgoingMailFiling.Sent, content.RawMime, CancellationToken.None)
                is { } copy)
            {
                return copy;
            }

            // Recorded rather than left silent: no later pass files a held account's sent copy, so this code on the
            // send is the only trace that one was owed.
            await this.filings.RecordFilingFailureAsync(
                record.Id,
                MailFathomErrorCode.OutgoingEmailFilingDestinationUnavailable,
                CancellationToken.None);

            return null;
        }
        catch (Exception failure)
        {
            await this.filings.RecordFilingFailureAsync(
                record.Id,
                MailboxCopyAppender.FailureCodeOf(failure),
                CancellationToken.None);

            return null;
        }
    }

    /// <summary>Files a prepared copy into the local folder playing its role, inside the transaction it belongs to.</summary>
    /// <param name="session">The transaction that settles the draft revision or the delivery.</param>
    /// <param name="copy">The prepared copy.</param>
    /// <param name="filedFrom">The outgoing record a sent copy is filed from, or <see langword="null" /> for a draft.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>What was filed, or <see langword="null" /> where the account is no longer held.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="copy" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The protected folders are supplied where the account is missing them, for the reason an arrival supplies them. The
    /// hierarchy is saved only then: a protected folder cannot be erased, so a filing that supplied nothing has no edit to
    /// conflict with, and saving anyway would make every draft save conflict with every arrival.
    /// </remarks>
    public async Task<FiledLocalEmail?> FileAsync(
        IPersistenceSession session,
        LocalMailCopy copy,
        OutgoingEmailId? filedFrom,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(copy);

        if (await this.folders.ReadAsync(session, copy.Account, cancellationToken)
            is not { Phase: MailAccountCustodyPhase.Held } holding)
        {
            return null;
        }

        var found = holding.ToTree();
        var missing = found.MissingProtectedFolders(this.MintId);
        var folder = found.With(missing).PlaceArrival(copy.Binding.Alias, copy.Filing.Role, sourceFolderName: null, this.MintId);

        if (missing.Count > 0)
        {
            await this.folders.SaveAsync(session, copy.Account, missing, [], cancellationToken);
        }

        var email = await this.emails.StoreFiledEmailAsync(
            session,
            copy.Account,
            copy.Binding,
            copy.Metadata,
            copy.Content.ByteLength,
            copy.Filing.Flags,
            filedFrom,
            cancellationToken);

        await this.contents.SaveContentAsync(session, email, occurrenceId: null, copy.Content, cancellationToken);
        await this.folders.PlaceAsync(session, copy.Account, email, folder.Folder, cancellationToken);

        return new FiledLocalEmail(copy.Account, copy.Binding.Alias, email, missing.Count > 0);
    }

    /// <summary>Erases a message filed earlier, inside the transaction that replaces or gives up what it showed.</summary>
    /// <param name="session">The transaction.</param>
    /// <param name="account">The account the message was filed for.</param>
    /// <param name="email">The message.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The alias it was stored under, or <see langword="null" /> where it was already gone.</returns>
    public Task<MailFolderAlias?> EraseAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        StoredEmailId email,
        CancellationToken cancellationToken) =>
        this.folders.EraseEmailAsync(session, account, email, cancellationToken);

    /// <summary>Tells the account's clients what a committed filing changed.</summary>
    /// <param name="filed">What was filed.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filed" /> is <see langword="null" />.</exception>
    /// <remarks>Announced only after the commit, because a message a rolled-back attempt filed is not one a client may be sent to read.</remarks>
    public void Announce(FiledLocalEmail filed)
    {
        ArgumentNullException.ThrowIfNull(filed);

        this.signals.Publish(ClientSignal.MailChanged(
            filed.Account,
            filed.Folder,
            filed.Replaced is { } replaced ? [filed.Email, replaced] : [filed.Email]));

        if (filed.CreatedFolders)
        {
            this.signals.Publish(ClientSignal.FoldersChanged(filed.Account));
        }
    }

    /// <summary>Tells the account's clients that a committed transaction erased a filed message.</summary>
    /// <param name="account">The account.</param>
    /// <param name="folder">The alias the message was stored under.</param>
    /// <param name="email">The message.</param>
    public void AnnounceErased(MailAccountIdentity account, MailFolderAlias folder, StoredEmailId email) =>
        this.signals.Publish(ClientSignal.MailChanged(account, folder, [email]));

    private LocalMailFolderId MintId() => LocalMailFolderId.Create(Guid.CreateVersion7(this.timeProvider.GetUtcNow()));
}
