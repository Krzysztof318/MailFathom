// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Changes one folder of a remote mailbox, on behalf of an act the mailbox owner authored.</summary>
/// <remarks>
/// <para>
/// This is the only type in MailFathom able to change a mailbox, and it is deliberately a different type from
/// <see cref="IMailboxSession" /> rather than a mode of it. Synchronization, reconciliation, content retrieval, and
/// every MCP tool reach the server through that one, which exposes no operation capable of writing; a refactor
/// therefore cannot give a read path the ability to write, because a read path never holds something that has it.
/// </para>
/// <para>
/// The surface is closed to exactly the mutations MailFathom is permitted to perform. There is no method that sends,
/// replies, or forwards, none that creates, renames, deletes, or subscribes to a folder, and none that writes
/// <c>\Answered</c> or <c>\Draft</c>. Permitting one of those later is a decision to reopen rather than a method to
/// append, and this surface is what a permitted mutation arrives on — <c>\Flagged</c> and the keywords did, because
/// each is a change to one message and therefore the same kind of act as the four that were here first. What does not
/// arrive here is an act of a different kind: folder creation is a port of its own for exactly that reason, so a caller
/// able to file a message into a folder is deliberately unable to create one, and the reverse.
/// </para>
/// <para>
/// Every operation names what the caller asked for and never how the server was made to do it. Which protocol
/// extension carried a relocation is a property of the server rather than of the change, so it reaches no caller and no
/// record above debug detail; a server without RFC 6851 <c>MOVE</c> behaves identically to one with it, from here up.
/// </para>
/// <para>
/// A relocation and a delete are not atomic on a server that lacks <c>MOVE</c>, and nothing here makes them so. A crash
/// between the commands leaves the mailbox in a state this session cannot describe, which is why every operation takes
/// an <see cref="IMailboxMutationJournal" />: the caller has written the change down before calling, the session
/// announces each stage of the sequence as it passes it, and a resumed attempt reads
/// <see cref="IMailboxMutationJournal.Stage" /> and continues from there instead of starting over.
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
    /// <param name="journal">The durable record of this deletion, which the session announces each stage to and resumes from.</param>
    /// <param name="cancellationToken">Cancels the deletion.</param>
    /// <returns>A task that completes when the server has removed the email.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="occurrenceId" /> does not belong to this session.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="journal" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxMutationUnsupportedException">Thrown when the server advertises no <c>UIDPLUS</c>, so no message-scoped expunge exists.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the deletion within its configured resilience budget.</exception>
    /// <exception cref="MailboxFolderRecreatedException">Thrown when a recovered connection reselected the folder with a different UIDVALIDITY.</exception>
    /// <remarks>
    /// What becomes of the local copy is not decided here. This operation says the message is gone from the server, and
    /// nothing else, so an account's disposition for mail somebody else deleted never silently governs mail MailFathom
    /// deleted. The remote <c>\Seen</c> flag is untouched.
    /// </remarks>
    Task DeleteAsync(
        EmailOccurrenceId occurrenceId,
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
}
