// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Domain.Mutations;

/// <summary>States one change somebody authored, in the form it is written down before any IMAP command is issued.</summary>
/// <remarks>
/// <para>
/// The request carries both identities of the email deliberately. <see cref="StoredEmailId" /> is the local email, which
/// is what ties the record to the mail it describes and what carries the record through that mail's deletion path;
/// <see cref="Occurrence" /> is where the email was when the change was asked for, which is what an IMAP command is
/// actually issued against and what must not drift when a later mutation moves the email somewhere else.
/// </para>
/// <para>
/// The parameters a mutation takes are part of the request rather than a payload beside it, and which ones are present
/// is an invariant rather than a convention: a relocation and a copy name a destination folder, a delete names what
/// becomes of the local copy and a relocation may, a flag change names a direction, and a keyword change names the
/// keywords it is about. The factories are the only way to build one, so a request naming a destination for a delete
/// cannot be constructed at all.
/// </para>
/// <para>
/// Nothing here is mail content. A folder path, an account, a folder binding, a UID, and a requester identity are all
/// MailFathom's own or the server's own names for things, which is what lets the durable record be written without the
/// message it is about.
/// </para>
/// </remarks>
public sealed record MailboxMutationRequest
{
    private MailboxMutationRequest(
        StoredEmailId storedEmailId,
        MailOwnerId owner,
        EmailOccurrenceId occurrence,
        MailboxMutation mutation,
        MailboxMutationRequester requester,
        RemoteFolderPath? destinationPath,
        bool? desiredSeenState,
        bool? desiredFlaggedState,
        AuthoredMailKeywords? keywords,
        AuthoredDeleteEmailDisposition? localDisposition)
    {
        this.StoredEmailId = storedEmailId;
        this.Owner = owner;
        this.Occurrence = occurrence;
        this.Mutation = mutation;
        this.Requester = requester;
        this.DestinationPath = destinationPath;
        this.DesiredSeenState = desiredSeenState;
        this.DesiredFlaggedState = desiredFlaggedState;
        this.Keywords = keywords;
        this.LocalDisposition = localDisposition;
    }

    /// <summary>Gets the local email the change is about.</summary>
    public StoredEmailId StoredEmailId { get; }

    /// <summary>Gets the owner whose account the change is about.</summary>
    /// <remarks>
    /// Named beside the occurrence rather than derived from it, because an occurrence names an account by the
    /// identifier an operator wrote and the record this request becomes says whose mailbox was changed. Whoever asked
    /// resolved the account through a catalog or read it off the mail's own row, so the answer travels with the request
    /// instead of being looked up where it is written down.
    /// </remarks>
    public MailOwnerId Owner { get; }

    /// <summary>Gets the remote occurrence the change was asked for, which is what the IMAP command targets.</summary>
    public EmailOccurrenceId Occurrence { get; }

    /// <summary>Gets the account the change is about, named by its owner and its identifier.</summary>
    public MailAccountIdentity Account => MailAccountIdentity.Create(this.Owner, this.Occurrence.AccountId);

    /// <summary>Gets the change that was asked for.</summary>
    public MailboxMutation Mutation { get; }

    /// <summary>Gets the authored act that asked.</summary>
    public MailboxMutationRequester Requester { get; }

    /// <summary>Gets the folder a relocation or a copy puts the email into, and <see langword="null" /> for every other mutation.</summary>
    public RemoteFolderPath? DestinationPath { get; }

    /// <summary>Gets which way a <c>\Seen</c> change was asked for, and <see langword="null" /> for every other mutation.</summary>
    public bool? DesiredSeenState { get; }

    /// <summary>Gets which way a <c>\Flagged</c> change was asked for, and <see langword="null" /> for every other mutation.</summary>
    public bool? DesiredFlaggedState { get; }

    /// <summary>Gets the keywords a keyword mutation names, and <see langword="null" /> for every other mutation.</summary>
    /// <remarks>
    /// What the set means is the mutation's to say rather than this property's: it is the keywords to put on the email,
    /// the keywords to take off it, or the keywords it should end up carrying. An empty set is meaningful for the last
    /// of those and refused for the other two, because clearing every keyword is something to ask for and adding none
    /// is not.
    /// </remarks>
    public AuthoredMailKeywords? Keywords { get; }

    /// <summary>Gets what becomes of the local copy once the change has happened, and <see langword="null" /> where nothing local is disposed of.</summary>
    /// <remarks>
    /// <para>
    /// It is resolved from the account's configuration when the request is built and written down with the record, so a
    /// setting changed while the change is in flight cannot decide the outcome of work already begun.
    /// </para>
    /// <para>
    /// A delete always carries one, and a relocation carries one exactly when its destination is a folder MailFathom does
    /// not mirror. Those are the two ways an occurrence leaves the mirrored mailbox for good: the message is somewhere
    /// nothing here will look at again, so what becomes of the local copy is the same question in both cases and is
    /// answered by the same setting rather than by a second one invented for the relocation. A relocation between
    /// mirrored folders carries none, because the row is carried into the destination folder instead, and a copy carries
    /// none because the source occurrence stays where it is.
    /// </para>
    /// </remarks>
    public AuthoredDeleteEmailDisposition? LocalDisposition { get; }

    /// <summary>Asks for one email to be moved out of its folder and into another.</summary>
    /// <param name="storedEmailId">The local email being moved.</param>
    /// <param name="owner">The owner whose account the change is about.</param>
    /// <param name="occurrence">Where the email is now.</param>
    /// <param name="requester">The authored act asking.</param>
    /// <param name="destinationPath">The folder to move it into.</param>
    /// <param name="localDisposition">
    /// What becomes of the local copy, supplied only when the destination is a folder MailFathom does not mirror, and
    /// <see langword="null" /> for a destination it does.
    /// </param>
    /// <returns>The request to write down.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="occurrence" /> or <paramref name="requester" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="localDisposition" /> names no declared disposition.</exception>
    /// <remarks>
    /// Nothing about the IMAP command changes with the disposition. The move is issued the same way into a folder
    /// MailFathom mirrors and one it does not, because the server's folder is the same kind of thing either way; what the
    /// disposition decides is only what synchronization does with the local row once the occurrence has been seen to
    /// leave its source folder, which for an unmirrored destination is the last MailFathom will ever see of it.
    /// </remarks>
    public static MailboxMutationRequest Relocate(
        StoredEmailId storedEmailId,
        MailOwnerId owner,
        EmailOccurrenceId occurrence,
        MailboxMutationRequester requester,
        RemoteFolderPath destinationPath,
        AuthoredDeleteEmailDisposition? localDisposition = null) => Create(
            storedEmailId,
            owner,
            occurrence,
            MailboxMutation.Relocate,
            requester,
            destinationPath,
            desiredSeenState: null,
            desiredFlaggedState: null,
            keywords: null,
            localDisposition);

    /// <summary>Asks for one email to be removed from the folder it is in.</summary>
    /// <param name="storedEmailId">The local email being removed.</param>
    /// <param name="owner">The owner whose account the change is about.</param>
    /// <param name="occurrence">Where the email is now.</param>
    /// <param name="requester">The authored act asking.</param>
    /// <param name="localDisposition">What becomes of the local copy once the server no longer holds the message.</param>
    /// <returns>The request to write down.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="occurrence" /> or <paramref name="requester" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="localDisposition" /> names no declared disposition.</exception>
    /// <remarks>
    /// The disposition is a parameter rather than something read where the delete completes, because completion happens
    /// in a later synchronization run that would read whatever the configuration says by then. Taking it here is what
    /// makes the answer the one that was true when the owner asked.
    /// </remarks>
    public static MailboxMutationRequest Delete(
        StoredEmailId storedEmailId,
        MailOwnerId owner,
        EmailOccurrenceId occurrence,
        MailboxMutationRequester requester,
        AuthoredDeleteEmailDisposition localDisposition) => Create(
            storedEmailId,
            owner,
            occurrence,
            MailboxMutation.Delete,
            requester,
            destinationPath: null,
            desiredSeenState: null,
            desiredFlaggedState: null,
            keywords: null,
            localDisposition);

    /// <summary>Asks for the remote <c>\Seen</c> flag of one email to be set or cleared.</summary>
    /// <param name="storedEmailId">The local email being flagged.</param>
    /// <param name="owner">The owner whose account the change is about.</param>
    /// <param name="occurrence">Where the email is now.</param>
    /// <param name="requester">The authored act asking.</param>
    /// <param name="isSeen"><see langword="true" /> to mark the email read; <see langword="false" /> to mark it unread.</param>
    /// <returns>The request to write down.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="occurrence" /> or <paramref name="requester" /> is <see langword="null" />.</exception>
    public static MailboxMutationRequest SetSeen(
        StoredEmailId storedEmailId,
        MailOwnerId owner,
        EmailOccurrenceId occurrence,
        MailboxMutationRequester requester,
        bool isSeen) => Create(
            storedEmailId,
            owner,
            occurrence,
            MailboxMutation.SetSeen,
            requester,
            destinationPath: null,
            isSeen,
            desiredFlaggedState: null,
            keywords: null,
            localDisposition: null);

    /// <summary>Asks for a second live occurrence of one email to be put into another folder.</summary>
    /// <param name="storedEmailId">The local email being copied.</param>
    /// <param name="owner">The owner whose account the change is about.</param>
    /// <param name="occurrence">Where the email is now.</param>
    /// <param name="requester">The authored act asking.</param>
    /// <param name="destinationPath">The folder to copy it into.</param>
    /// <returns>The request to write down.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="occurrence" /> or <paramref name="requester" /> is <see langword="null" />.</exception>
    public static MailboxMutationRequest Copy(
        StoredEmailId storedEmailId,
        MailOwnerId owner,
        EmailOccurrenceId occurrence,
        MailboxMutationRequester requester,
        RemoteFolderPath destinationPath) => Create(
            storedEmailId,
            owner,
            occurrence,
            MailboxMutation.Copy,
            requester,
            destinationPath,
            desiredSeenState: null,
            desiredFlaggedState: null,
            keywords: null,
            localDisposition: null);

    /// <summary>Asks for the remote <c>\Flagged</c> flag of one email to be set or cleared.</summary>
    /// <param name="storedEmailId">The local email being flagged.</param>
    /// <param name="owner">The owner whose account the change is about.</param>
    /// <param name="occurrence">Where the email is now.</param>
    /// <param name="requester">The authored act asking.</param>
    /// <param name="isFlagged"><see langword="true" /> to flag the email; <see langword="false" /> to clear the flag.</param>
    /// <returns>The request to write down.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="occurrence" /> or <paramref name="requester" /> is <see langword="null" />.</exception>
    public static MailboxMutationRequest SetFlagged(
        StoredEmailId storedEmailId,
        MailOwnerId owner,
        EmailOccurrenceId occurrence,
        MailboxMutationRequester requester,
        bool isFlagged) => Create(
            storedEmailId,
            owner,
            occurrence,
            MailboxMutation.SetFlagged,
            requester,
            destinationPath: null,
            desiredSeenState: null,
            isFlagged,
            keywords: null,
            localDisposition: null);

    /// <summary>Asks for keywords to be put on one email, beside whatever it already carries.</summary>
    /// <param name="storedEmailId">The local email being labelled.</param>
    /// <param name="owner">The owner whose account the change is about.</param>
    /// <param name="occurrence">Where the email is now.</param>
    /// <param name="requester">The authored act asking.</param>
    /// <param name="keywords">The keywords to put on it, which must name at least one.</param>
    /// <returns>The request to write down.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keywords" /> names none.</exception>
    public static MailboxMutationRequest AddKeywords(
        StoredEmailId storedEmailId,
        MailOwnerId owner,
        EmailOccurrenceId occurrence,
        MailboxMutationRequester requester,
        AuthoredMailKeywords keywords)
    {
        // Guarded here rather than left to the parameter check below, which reads a null keyword set as a mutation that
        // names none and would refuse it as the wrong mutation instead of as the missing argument it is.
        ArgumentNullException.ThrowIfNull(keywords);

        return Create(
            storedEmailId,
            owner,
            occurrence,
            MailboxMutation.AddKeywords,
            requester,
            destinationPath: null,
            desiredSeenState: null,
            desiredFlaggedState: null,
            keywords,
            localDisposition: null);
    }

    /// <summary>Asks for keywords to be taken off one email, leaving the ones it is not asked about.</summary>
    /// <param name="storedEmailId">The local email being relabelled.</param>
    /// <param name="owner">The owner whose account the change is about.</param>
    /// <param name="occurrence">Where the email is now.</param>
    /// <param name="requester">The authored act asking.</param>
    /// <param name="keywords">The keywords to take off it, which must name at least one.</param>
    /// <returns>The request to write down.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keywords" /> names none.</exception>
    public static MailboxMutationRequest RemoveKeywords(
        StoredEmailId storedEmailId,
        MailOwnerId owner,
        EmailOccurrenceId occurrence,
        MailboxMutationRequester requester,
        AuthoredMailKeywords keywords)
    {
        // Guarded here rather than left to the parameter check below, which reads a null keyword set as a mutation that
        // names none and would refuse it as the wrong mutation instead of as the missing argument it is.
        ArgumentNullException.ThrowIfNull(keywords);

        return Create(
            storedEmailId,
            owner,
            occurrence,
            MailboxMutation.RemoveKeywords,
            requester,
            destinationPath: null,
            desiredSeenState: null,
            desiredFlaggedState: null,
            keywords,
            localDisposition: null);
    }

    /// <summary>Asks for one email's keywords to become exactly the set that was named.</summary>
    /// <param name="storedEmailId">The local email being relabelled.</param>
    /// <param name="owner">The owner whose account the change is about.</param>
    /// <param name="occurrence">Where the email is now.</param>
    /// <param name="requester">The authored act asking.</param>
    /// <param name="keywords">The keywords it should end up carrying, which may name none and then clears them all.</param>
    /// <returns>The request to write down.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static MailboxMutationRequest SetKeywords(
        StoredEmailId storedEmailId,
        MailOwnerId owner,
        EmailOccurrenceId occurrence,
        MailboxMutationRequester requester,
        AuthoredMailKeywords keywords)
    {
        // Guarded here rather than left to the parameter check below, which reads a null keyword set as a mutation that
        // names none and would refuse it as the wrong mutation instead of as the missing argument it is.
        ArgumentNullException.ThrowIfNull(keywords);

        return Create(
            storedEmailId,
            owner,
            occurrence,
            MailboxMutation.SetKeywords,
            requester,
            destinationPath: null,
            desiredSeenState: null,
            desiredFlaggedState: null,
            keywords,
            localDisposition: null);
    }

    /// <summary>Restores the request a durable record was written for.</summary>
    /// <param name="storedEmailId">The local email the change is about.</param>
    /// <param name="owner">The owner whose account the change is about.</param>
    /// <param name="occurrence">The occurrence the change was asked for.</param>
    /// <param name="mutation">The change that was asked for.</param>
    /// <param name="requester">The authored act that asked.</param>
    /// <param name="destinationPath">The stored destination folder, where the mutation takes one.</param>
    /// <param name="desiredSeenState">The stored <c>\Seen</c> direction, where the mutation takes one.</param>
    /// <param name="desiredFlaggedState">The stored <c>\Flagged</c> direction, where the mutation takes one.</param>
    /// <param name="keywords">The stored keywords, where the mutation takes them.</param>
    /// <param name="localDisposition">The stored local disposition, where the mutation takes one.</param>
    /// <returns>The request those values name.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="occurrence" /> or <paramref name="requester" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="mutation" /> is unspecified, or when the parameters present are not the ones it takes.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="localDisposition" /> names no declared disposition.</exception>
    /// <remarks>
    /// This is the one route that accepts the parameters loose, because a stored row hands them back that way. It
    /// validates the shape the factories above guarantee, so a row edited by hand into a combination no mutation has is
    /// rejected on the way in rather than acted on.
    /// </remarks>
    public static MailboxMutationRequest Create(
        StoredEmailId storedEmailId,
        MailOwnerId owner,
        EmailOccurrenceId occurrence,
        MailboxMutation mutation,
        MailboxMutationRequester requester,
        RemoteFolderPath? destinationPath,
        bool? desiredSeenState,
        bool? desiredFlaggedState,
        AuthoredMailKeywords? keywords,
        AuthoredDeleteEmailDisposition? localDisposition)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentNullException.ThrowIfNull(requester);

        if (!mutation.IsSpecified)
        {
            throw new ArgumentException("A mutation request must name a permitted mutation.", nameof(mutation));
        }

        RequireParametersOf(mutation, destinationPath, desiredSeenState, desiredFlaggedState, keywords, localDisposition);

        return new MailboxMutationRequest(
            storedEmailId,
            owner,
            occurrence,
            mutation,
            requester,
            destinationPath,
            desiredSeenState,
            desiredFlaggedState,
            keywords,
            localDisposition);
    }

    /// <summary>Refuses a parameter set that is not the one the named mutation takes.</summary>
    private static void RequireParametersOf(
        MailboxMutation mutation,
        RemoteFolderPath? destinationPath,
        bool? desiredSeenState,
        bool? desiredFlaggedState,
        AuthoredMailKeywords? keywords,
        AuthoredDeleteEmailDisposition? localDisposition)
    {
        var takesDestination = mutation == MailboxMutation.Relocate || mutation == MailboxMutation.Copy;
        var takesSeenState = mutation == MailboxMutation.SetSeen;
        var takesFlaggedState = mutation == MailboxMutation.SetFlagged;
        var takesKeywords = mutation == MailboxMutation.AddKeywords
            || mutation == MailboxMutation.RemoveKeywords
            || mutation == MailboxMutation.SetKeywords;

        // A delete has to name one and a relocation may, which is why this is two checks rather than one equality: the
        // relocation's disposition says its destination is unmirrored, and its absence says the destination is mirrored.
        // Both are meaningful, so neither can be refused.
        var requiresLocalDisposition = mutation == MailboxMutation.Delete;
        var permitsLocalDisposition = requiresLocalDisposition || mutation == MailboxMutation.Relocate;

        if (takesDestination != destinationPath.HasValue)
        {
            throw new ArgumentException(
                takesDestination
                    ? $"The {mutation.Name} mutation names a destination folder and none was supplied."
                    : $"The {mutation.Name} mutation names no destination folder.",
                nameof(destinationPath));
        }

        if (takesSeenState != desiredSeenState.HasValue)
        {
            throw new ArgumentException(
                takesSeenState
                    ? $"The {mutation.Name} mutation names a \\Seen direction and none was supplied."
                    : $"The {mutation.Name} mutation names no \\Seen direction.",
                nameof(desiredSeenState));
        }

        if (takesFlaggedState != desiredFlaggedState.HasValue)
        {
            throw new ArgumentException(
                takesFlaggedState
                    ? $"The {mutation.Name} mutation names a \\Flagged direction and none was supplied."
                    : $"The {mutation.Name} mutation names no \\Flagged direction.",
                nameof(desiredFlaggedState));
        }

        if (takesKeywords != (keywords is not null))
        {
            throw new ArgumentException(
                takesKeywords
                    ? $"The {mutation.Name} mutation names keywords and none were supplied."
                    : $"The {mutation.Name} mutation names no keywords.",
                nameof(keywords));
        }

        // Clearing every keyword is something to ask for and adding none is not, so the empty set is meaningful for one
        // of the three mutations and a mistake in the other two. Refusing it here is what keeps a rule whose keyword
        // list was mistyped away from a durable record of a change that would perform nothing.
        if (keywords is { IsEmpty: true } && mutation != MailboxMutation.SetKeywords)
        {
            throw new ArgumentException(
                $"The {mutation.Name} mutation must name at least one keyword.",
                nameof(keywords));
        }

        if (requiresLocalDisposition && !localDisposition.HasValue)
        {
            throw new ArgumentException(
                $"The {mutation.Name} mutation names a local disposition and none was supplied.",
                nameof(localDisposition));
        }

        if (!permitsLocalDisposition && localDisposition.HasValue)
        {
            throw new ArgumentException(
                $"The {mutation.Name} mutation names no local disposition.",
                nameof(localDisposition));
        }

        // A value outside the declared set names no decision about the local copy, and the request is what gets written
        // down: a record carrying one could not be read back at all, which would strand the delete rather than perform
        // it under some fallback. It is refused where the request is built, before any of that is durable.
        if (localDisposition is { } disposition && !Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(localDisposition),
                disposition,
                "The local disposition of a delete must be one of the declared dispositions.");
        }
    }
}
