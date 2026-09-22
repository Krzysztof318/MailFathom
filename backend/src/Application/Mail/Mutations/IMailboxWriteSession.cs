// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Changes one folder of a remote mailbox, on behalf of an act the mailbox user authored.</summary>
/// <remarks>
/// <para>
/// This is the only type in MailFathom able to change a mailbox, and it is deliberately a different type from
/// <see cref="IMailboxSession" /> rather than a mode of it. Synchronization, reconciliation, content retrieval, and
/// every MCP tool reach the server through that one, which exposes no operation capable of writing; a refactor
/// therefore cannot give a read path the ability to write, because a read path never holds something that has it.
/// </para>
/// <para>
/// The surface is closed to exactly the mutations MailFathom is permitted to perform. There is no method that sends,
/// replies, or forwards, none that creates, renames, deletes, or subscribes to a folder, and none that changes whether
/// a message on the source counts as answered. <see cref="AppendRestoredAsync" /> does put <c>\Answered</c> onto a
/// message, and it is not an exception to that: it states what the source itself last showed about a message
/// MailFathom is handing back, rather than asserting anything new about one the source already holds. Permitting one
/// of the others later is a decision to reopen rather than a method to append, and this
/// surface is what a permitted mutation arrives on — <c>\Flagged</c> and the keywords did, because each is a change to
/// one message and therefore the same kind of act as the four that were here first. What does not arrive here is an act
/// of a different kind: folder creation is a port of its own for exactly that reason, so a caller able to file a message
/// into a folder is deliberately unable to create one, and the reverse.
/// </para>
/// <para>
/// <see cref="AppendAsync" /> and <see cref="WithdrawAppendedAsync" /> are the third reopening, and they are narrower
/// than they look. Both act only on a message MailFathom itself composed and holds the outgoing record of: the append
/// puts a copy of it into the folder its state calls for, and the withdrawal takes back a copy the append put there.
/// Neither can reach a message somebody sent to this mailbox, because neither takes an occurrence — an append names no
/// message at all and a withdrawal names a UID the append itself reported. That is also why <c>\Draft</c> is writable
/// here and nowhere else: it is an assertion about a message being composed, which is true of exactly these and of
/// nothing a user received.
/// </para>
/// <para>
/// Every operation names what the caller asked for and never how the server was made to do it. Which protocol
/// extension carried a relocation is a property of the server rather than of the change, so it reaches no caller and no
/// record above debug detail; a server without RFC 6851 <c>MOVE</c> behaves identically to one with it, from here up.
/// </para>
/// <para>
/// A relocation and a delete are not atomic on a server that lacks <c>MOVE</c>, and nothing here makes them so. A crash
/// between the commands leaves the mailbox in a state this session cannot describe, which is why every operation that
/// changes a message the user already has takes an <see cref="IMailboxMutationJournal" />: the caller has written the
/// change down before calling, the session announces each stage of the sequence as it passes it, and a resumed attempt
/// reads <see cref="IMailboxMutationJournal.Stage" /> and continues from there instead of starting over.
/// </para>
/// <para>
/// Four operations take none, and each rests on a durable record of its own instead.
/// <see cref="AppendAsync" /> and <see cref="WithdrawAppendedAsync" /> put a copy MailFathom composed into a folder and
/// take it back again, and the outgoing or draft record the caller wrote before calling is what a resumed attempt
/// reads; <see cref="AppendRestoredAsync" /> puts a held message back onto its source, and the restore's own append
/// record is what says an unanswered one must not be issued again.
/// <see cref="ExpungeDrainedAsync" /> authors nothing at all: both its commands are idempotent against the UIDs
/// they name, and the row that selected each UID still carries the occurrence until the expunge is answered — so the
/// durable record a journal would add is one the selecting state already holds, per message of a whole mailbox.
/// </para>
/// <para>
/// Resuming is decided here rather than by the caller because it depends on what the connection advertises, which is
/// this adapter's business and deliberately reaches no layer above. What the caller decides is the one thing the
/// protocol cannot: a mutation whose placement command was issued and never acknowledged never reaches this session at
/// all, because issuing it again would put a second message in the destination folder.
/// </para>
/// <para>
/// One session is used by one caller at a time and is not safe for concurrent use. It is short-lived by design: it
/// holds the account's single write connection for as long as it is open, so a second caller waits.
/// </para>
/// </remarks>
public interface IMailboxWriteSession : IAsyncDisposable
{
    /// <summary>Moves one email out of this session's folder and into another folder of the same account.</summary>
    /// <param name="occurrenceId">The occurrence to move, which must belong to this session's account, folder, and UIDVALIDITY.</param>
    /// <param name="destinationPath">The remote path of the folder to move it into.</param>
    /// <param name="journal">The durable record of this relocation, which the session announces each stage to and resumes from.</param>
    /// <param name="cancellationToken">Cancels the relocation.</param>
    /// <returns>Where the destination folder put the email, when the server named it.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="occurrenceId" /> does not belong to this session.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="journal" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxMutationUnsupportedException">Thrown when the server advertises neither <c>MOVE</c> nor the <c>UIDPLUS</c> the fallback needs to remove only the moved message.</exception>
    /// <exception cref="MailboxDestinationFolderMissingException">Thrown when the server holds no folder at <paramref name="destinationPath" />, which is settled rather than deferred.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the relocation within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// The remote <c>\Seen</c> flag of the email is not part of this operation and is left exactly as the server holds
    /// it, on either protocol path. Filing a message is not a statement that anyone read it.
    /// </remarks>
    Task<RemoteEmailPlacement> RelocateAsync(
        EmailOccurrenceId occurrenceId,
        RemoteFolderPath destinationPath,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken);

    /// <summary>Removes one email from this session's folder on the server.</summary>
    /// <param name="occurrenceId">The occurrence to remove, which must belong to this session's account, folder, and UIDVALIDITY.</param>
    /// <param name="serverDisposition">Whether the email is expunged or only flagged <c>\Deleted</c>, as the delete was recorded under.</param>
    /// <param name="journal">The durable record of this deletion, which the session announces each stage to and resumes from.</param>
    /// <param name="cancellationToken">Cancels the deletion.</param>
    /// <returns>A task that completes when the server has removed the email, or flagged it where only that was asked for.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="occurrenceId" /> does not belong to this session.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="journal" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxMutationUnsupportedException">
    /// Thrown when the server advertises no <c>UIDPLUS</c>, so no message-scoped expunge exists. A delete that only flags
    /// is refused on the same server as well, so what a server can delete does not depend on an account setting.
    /// </exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the deletion within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// What becomes of the local copy is not decided here. This operation says what the server now does with the
    /// message, and nothing else, so an account's disposition for mail somebody else deleted never silently governs mail
    /// MailFathom deleted. The remote <c>\Seen</c> flag is untouched.
    /// </remarks>
    Task DeleteAsync(
        EmailOccurrenceId occurrenceId,
        AuthoredDeleteServerDisposition serverDisposition,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken);

    /// <summary>Sets or clears the remote <c>\Seen</c> flag of one email in this session's folder.</summary>
    /// <param name="occurrenceId">The occurrence to flag, which must belong to this session's account, folder, and UIDVALIDITY.</param>
    /// <param name="isSeen"><see langword="true" /> to mark the email read; <see langword="false" /> to mark it unread.</param>
    /// <param name="journal">The durable record of this flag change, which exists for provenance rather than for retry safety.</param>
    /// <param name="cancellationToken">Cancels the flag write.</param>
    /// <returns>A task that completes when the server has recorded the flag.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="occurrenceId" /> does not belong to this session.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="journal" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the flag write within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// Both directions are the same mutation, because both are the same authored act about the same flag. This is the
    /// only operation in MailFathom that writes <c>\Seen</c>; the stored value stays a snapshot of what the server
    /// reports, written by synchronization observing the result rather than by this call.
    /// </remarks>
    Task SetSeenAsync(
        EmailOccurrenceId occurrenceId,
        bool isSeen,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken);

    /// <summary>Puts a second live occurrence of one email into another folder of the same account.</summary>
    /// <param name="occurrenceId">The occurrence to copy, which must belong to this session's account, folder, and UIDVALIDITY.</param>
    /// <param name="destinationPath">The remote path of the folder to copy it into.</param>
    /// <param name="journal">The durable record of this copy, which is announced before the command goes out.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>Where the destination folder put the email, when the server named it.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="occurrenceId" /> does not belong to this session.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="journal" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxDestinationFolderMissingException">Thrown when the server holds no folder at <paramref name="destinationPath" />, which is settled rather than deferred.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the copy within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// A copy issued twice is a second message rather than a repeat of the first, so this operation is never repeated
    /// on the caller's behalf. The source occurrence is unchanged, including its remote <c>\Seen</c> flag.
    /// </remarks>
    Task<RemoteEmailPlacement> CopyAsync(
        EmailOccurrenceId occurrenceId,
        RemoteFolderPath destinationPath,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken);

    /// <summary>Sets or clears the remote <c>\Flagged</c> flag of one email in this session's folder.</summary>
    /// <param name="occurrenceId">The occurrence to flag, which must belong to this session's account, folder, and UIDVALIDITY.</param>
    /// <param name="isFlagged"><see langword="true" /> to flag the email; <see langword="false" /> to clear the flag.</param>
    /// <param name="journal">The durable record of this flag change, which exists for provenance rather than for retry safety.</param>
    /// <param name="cancellationToken">Cancels the flag write.</param>
    /// <returns>A task that completes when the server has recorded the flag.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="occurrenceId" /> does not belong to this session.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="journal" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the flag write within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// The <c>\Seen</c> flag is not part of this operation. Both write one flag and leave every other one exactly as the
    /// server holds it, which is what keeps each of them the answer to the question it was asked.
    /// </remarks>
    Task SetFlaggedAsync(
        EmailOccurrenceId occurrenceId,
        bool isFlagged,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken);

    /// <summary>Puts keywords on one email in this session's folder, beside the ones it already carries.</summary>
    /// <param name="occurrenceId">The occurrence to label, which must belong to this session's account, folder, and UIDVALIDITY.</param>
    /// <param name="keywords">The keywords to put on it.</param>
    /// <param name="journal">The durable record of this keyword change, which exists for provenance rather than for retry safety.</param>
    /// <param name="cancellationToken">Cancels the keyword write.</param>
    /// <returns>A task that completes when the server has recorded the keywords.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="occurrenceId" /> does not belong to this session.</exception>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="MailboxMutationUnsupportedException">Thrown when the folder will not keep a keyword it was not already keeping.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the keyword write within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// A keyword the email already carries is asked for again rather than filtered out, because a <c>STORE +FLAGS</c> is
    /// idempotent for one UID and reading the message first to avoid it would buy a round trip and a race.
    /// </remarks>
    Task AddKeywordsAsync(
        EmailOccurrenceId occurrenceId,
        AuthoredMailKeywords keywords,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken);

    /// <summary>Takes keywords off one email in this session's folder, leaving the ones it was not asked about.</summary>
    /// <param name="occurrenceId">The occurrence to relabel, which must belong to this session's account, folder, and UIDVALIDITY.</param>
    /// <param name="keywords">The keywords to take off it.</param>
    /// <param name="journal">The durable record of this keyword change, which exists for provenance rather than for retry safety.</param>
    /// <param name="cancellationToken">Cancels the keyword write.</param>
    /// <returns>A task that completes when the server has recorded the removal.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="occurrenceId" /> does not belong to this session.</exception>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the keyword write within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// A removal needs nothing of the folder that a keyword it never kept would need, so this is the one keyword
    /// operation that is never refused for what the folder will store: taking off a keyword that is not there is what
    /// the server already reports as success.
    /// </remarks>
    Task RemoveKeywordsAsync(
        EmailOccurrenceId occurrenceId,
        AuthoredMailKeywords keywords,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken);

    /// <summary>Makes one email's keywords exactly the set that was named, in this session's folder.</summary>
    /// <param name="occurrenceId">The occurrence to relabel, which must belong to this session's account, folder, and UIDVALIDITY.</param>
    /// <param name="keywords">The keywords it should end up carrying, which may be none and then clears them all.</param>
    /// <param name="journal">The durable record of this keyword change, which exists for provenance rather than for retry safety.</param>
    /// <param name="cancellationToken">Cancels the keyword write.</param>
    /// <returns>A task that completes when the server has recorded the set.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="occurrenceId" /> does not belong to this session.</exception>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="MailboxMutationUnsupportedException">Thrown when the folder will not keep a keyword it was not already keeping.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the keyword write within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// This never issues the <c>STORE FLAGS</c> the operation's name suggests. That command replaces a message's entire
    /// flag set, so it would clear <c>\Seen</c>, <c>\Flagged</c>, <c>\Answered</c>, and <c>\Draft</c> as a side effect of
    /// writing a label — flags this system either owns through one deliberate operation each or refuses to write at all.
    /// What the implementation does instead is its own business; what this contract promises is that only keywords move.
    /// </remarks>
    Task SetKeywordsAsync(
        EmailOccurrenceId occurrenceId,
        AuthoredMailKeywords keywords,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken);

    /// <summary>Puts a copy of a message MailFathom composed into this session's folder.</summary>
    /// <param name="rawMime">The stored RFC 822 bytes to append, which are the bytes that were or will be transmitted.</param>
    /// <param name="flags">The flags the appended copy carries, which the message's own state decides.</param>
    /// <param name="internalDate">The internal date the server records for the copy, which is what a mail client sorts the folder by.</param>
    /// <param name="cancellationToken">Cancels the append.</param>
    /// <returns>What the server said about the copy it accepted.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rawMime" /> is empty.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the append within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// <para>
    /// This is the one operation here that creates a message rather than changing one, and the one that takes no
    /// occurrence — an <c>APPEND</c> names a folder and a body, so there is no message of the user's it could reach.
    /// The folder is this session's own selection rather than an argument, which is what keeps a caller from appending
    /// into a folder it did not open the session for.
    /// </para>
    /// <para>
    /// It is never repeated on the caller's behalf, for the reason a copy is not: an <c>APPEND</c> issued twice is a
    /// second message in the user's folder rather than a repeat of the first, and nothing the folder shows afterwards
    /// tells them apart. There is no journal here because the durable record of the append is the caller's outgoing
    /// record, which is written before this is called and confirmed after it returns.
    /// </para>
    /// <para>
    /// The bytes are appended as they were stored rather than recomposed, so the filed copy is the message that was
    /// delivered. Nothing about this reads or writes any other message's flags.
    /// </para>
    /// </remarks>
    Task<AppendedMailCopy> AppendAsync(
        ReadOnlyMemory<byte> rawMime,
        AppendedMailFlags flags,
        DateTimeOffset internalDate,
        CancellationToken cancellationToken);

    /// <summary>Takes a copy this session's folder was given by an earlier append back out of it.</summary>
    /// <param name="uidValidity">The UIDVALIDITY the append reported, which must still be the folder's.</param>
    /// <param name="uid">The UID the append reported for the copy.</param>
    /// <param name="cancellationToken">Cancels the withdrawal.</param>
    /// <returns>A task that completes when the folder no longer holds the copy.</returns>
    /// <exception cref="MailboxMutationUnsupportedException">Thrown when the server advertises no <c>UIDPLUS</c>, so no message-scoped expunge exists.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the withdrawal within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when the folder no longer reports the UIDVALIDITY the append named.</exception>
    /// <remarks>
    /// <para>
    /// It exists so a mirrored copy of an undelivered message does not outlive the wait it was showing. The UID is one
    /// the server itself named when it accepted the copy, so this reaches a message MailFathom put there and can reach
    /// no other — which is what separates it from the delete a mutation performs on the user's own mail.
    /// </para>
    /// <para>
    /// The UIDVALIDITY is compared before anything is issued, because a folder recreated since the append renumbered
    /// every message in it and the recorded UID would name somebody else's. A bare <c>EXPUNGE</c> is never issued, so a
    /// server without <c>UID EXPUNGE</c> is refused rather than served: removing every message anybody flagged
    /// <c>\Deleted</c> is not a side effect this may have.
    /// </para>
    /// <para>
    /// A copy the folder no longer holds is not an error. The user deleting it themselves is the ordinary case, and
    /// what was asked for — that the copy is gone — is already true.
    /// </para>
    /// </remarks>
    Task WithdrawAppendedAsync(
        ImapUidValidity uidValidity,
        ImapUid uid,
        CancellationToken cancellationToken);

    /// <summary>Puts a held message back into this session's folder when the account's custody returns to mirroring.</summary>
    /// <param name="rawMime">The stored RFC 822 bytes to append, which are the bytes the source delivered when it was first stored.</param>
    /// <param name="state">The flags and keywords MailFathom holds for the message, which the copy carries onto the source.</param>
    /// <param name="internalDate">The arrival the stored message recorded, so a restored folder sorts as it did before.</param>
    /// <param name="cancellationToken">Cancels the append.</param>
    /// <returns>Where the folder put the copy, when the server named it.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rawMime" /> is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="state" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the append within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// <para>
    /// The fifth reopening of this surface, and the one act here that puts a message somebody else sent back where it
    /// came from. It is a method of its own rather than a mode of <see cref="AppendAsync" /> because the two assert
    /// different things: that one appends a copy MailFathom composed, carrying the two flags a composition establishes,
    /// while this one appends mail MailFathom was holding and carries what the source last showed about it together
    /// with what a person did to it while it was held. So this operation carries four system flags — <c>\Seen</c>,
    /// <c>\Answered</c>, <c>\Flagged</c> and <c>\Draft</c> — and the keywords beside them, each of which MailFathom
    /// observed per message and would otherwise lose on the way back. <c>\Deleted</c> is the one it does not carry,
    /// because it is a request that the folder stop holding the message rather than an observation about it.
    /// </para>
    /// <para>
    /// It is never repeated on the caller's behalf, for the reason <see cref="AppendAsync" /> is not: an <c>APPEND</c>
    /// issued twice is a second message in the user's folder. The durable record the restore writes before calling is
    /// what a resumed attempt reads, and a record whose answer never arrived is reported to an operator rather than
    /// issued again. See
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
    /// </para>
    /// </remarks>
    Task<RemoteEmailPlacement> AppendRestoredAsync(
        ReadOnlyMemory<byte> rawMime,
        RestoredEmailState state,
        DateTimeOffset internalDate,
        CancellationToken cancellationToken);

    /// <summary>Reports whether this source can be drained at all, which is whether it advertises <c>UIDPLUS</c>.</summary>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns><see langword="true" /> where a message-scoped expunge exists on this server.</returns>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server could not be reached to open the session the reading is taken from.</exception>
    /// <remarks>
    /// Asked before an account is moved into holding its own mailbox, and never as part of a batch. A source with no
    /// <c>UIDPLUS</c> has no way to remove one named message, so an account held against one would have its remote
    /// deletions switched off while its source was never emptied — it stays mirrored instead, and this is what
    /// establishes that. It reads the capabilities the open session already carries and issues no command of its own,
    /// which is not the same as being infallible: establishing or recovering the connection the reading is taken from
    /// fails against an away source exactly as every command in the session does, and the caller answers a source it
    /// could not reach by moving nothing and asking again next run.
    /// </remarks>
    Task<bool> SupportsDrainAsync(CancellationToken cancellationToken);

    /// <summary>Removes from this session's folder exactly the messages a drain selected, and no others.</summary>
    /// <param name="uidValidity">The UIDVALIDITY the selected occurrences name, which must still be the folder's.</param>
    /// <param name="uids">The UIDs to remove, which the drain gate selected from what MailFathom durably holds.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes when the folder no longer holds those messages.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="uids" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="uids" /> is empty.</exception>
    /// <exception cref="MailboxMutationUnsupportedException">Thrown when the server advertises no <c>UIDPLUS</c>, so no message-scoped expunge exists.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the removal within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when the folder no longer reports the UIDVALIDITY the occurrences name.</exception>
    /// <remarks>
    /// <para>
    /// The fourth reopening of this surface, and the one act here nobody authored message by message: an administrator
    /// switched the account into holding its own mailbox, and this empties the source of what MailFathom verifiably
    /// holds. It is reached only from that mode's background pass, and every UID it names passed a gate that read the
    /// stored payload back against its recorded length and digest first.
    /// </para>
    /// <para>
    /// It is <c>UID STORE +FLAGS</c> adding <c>\Deleted</c> over exactly these UIDs, followed by <c>UID EXPUNGE</c>
    /// naming exactly these UIDs. A bare <c>EXPUNGE</c> is never issued, so a message another client flagged
    /// <c>\Deleted</c> and MailFathom never stored is not swept along with the batch, and a server without
    /// <c>UID EXPUNGE</c> is refused rather than served. The UIDVALIDITY is compared as the folder reports it now,
    /// because a folder recreated since the batch was selected renumbered every message in it and these UIDs would
    /// name somebody else's mail.
    /// </para>
    /// <para>
    /// Both commands are idempotent against the UIDs they name, so a batch whose answer never arrived is issued again
    /// by the next pass rather than being settled from what the mailbox shows. A UID the folder no longer holds is not
    /// an error: what was asked for is already true. See
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
    /// </para>
    /// </remarks>
    Task ExpungeDrainedAsync(
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        CancellationToken cancellationToken);
}
