// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Rules.Actions;

/// <summary>One change a matching rule asks for, named as the mutation it will be requested through.</summary>
/// <remarks>
/// <para>
/// The set an action may name is exactly <see cref="MailboxMutation.All" />, because a rule asks for a change through
/// the same record every other requester uses and nothing here issues a command of its own. What this adds over the
/// mutation is the parameter the rule declared: a relocation and a copy name a destination folder, a flag change names
/// a direction, a keyword change names the keywords, and a delete names none of them.
/// </para>
/// <para>
/// The destination is a reference rather than a remote path, because MailFathom's own names for a folder — its alias
/// and the role it plays — survive the server renaming what they are bound to. Which folder the reference means, and
/// then which path that folder currently has, are both resolved when the request is written, which is what makes a
/// destination that has stopped being resolvable fail visibly instead of being filed somewhere else.
/// </para>
/// <para>
/// The factories are the only way to build one, so an action carrying a parameter its mutation does not take cannot be
/// constructed — the same invariant <see cref="MailboxMutationRequest" /> holds one layer down.
/// </para>
/// </remarks>
public sealed record MailRuleAction
{
    /// <summary>What separates two keywords in a rendered action, chosen as the one character none of them may hold.</summary>
    /// <remarks>
    /// An IMAP atom excludes the space and permits a comma, so joining on a comma would render <c>["a,b"]</c> and
    /// <c>["a", "b"]</c> identically — two different rule sets with one revision, where editing between them would read
    /// as no edit and leave the mutations of the first counted as already performed.
    /// </remarks>
    private const char KeywordSeparator = ' ';

    private MailRuleAction(
        MailboxMutation mutation,
        MailFolderReference? destination,
        bool? desiredSeenState,
        bool? desiredFlaggedState,
        AuthoredMailKeywords? keywords)
    {
        this.Mutation = mutation;
        this.Destination = destination;
        this.DesiredSeenState = desiredSeenState;
        this.DesiredFlaggedState = desiredFlaggedState;
        this.Keywords = keywords;
    }

    /// <summary>Gets the change this action asks for.</summary>
    public MailboxMutation Mutation { get; }

    /// <summary>Gets how a relocation or a copy names its folder, and <see langword="null" /> for every other action.</summary>
    public MailFolderReference? Destination { get; }

    /// <summary>Gets which way a <c>\Seen</c> change was asked for, and <see langword="null" /> for every other action.</summary>
    public bool? DesiredSeenState { get; }

    /// <summary>Gets which way a <c>\Flagged</c> change was asked for, and <see langword="null" /> for every other action.</summary>
    public bool? DesiredFlaggedState { get; }

    /// <summary>Gets the keywords a keyword action names, and <see langword="null" /> for every other action.</summary>
    public AuthoredMailKeywords? Keywords { get; }

    /// <summary>Gets the form this action is rendered in for a rule set's derived revision identity.</summary>
    /// <remarks>
    /// <para>
    /// It names the mutation and its parameter, so editing either moves the revision and the edited rule asks afresh
    /// rather than being read as the request already performed.
    /// </para>
    /// <para>
    /// A keyword set renders in the order the value holds it, which is its comparison order rather than the order the
    /// operator wrote. Reordering a keyword list is therefore not an edit, which is the right answer: the set means the
    /// same thing either way, and moving the revision would make every rule in the file ask afresh over a change to
    /// none of them.
    /// </para>
    /// </remarks>
    public string CanonicalForm => this switch
    {
        { Destination: { } destination } => string.Create(
            CultureInfo.InvariantCulture,
            $"{this.Mutation.Name}={destination}"),
        { DesiredSeenState: { } isSeen } => string.Create(
            CultureInfo.InvariantCulture,
            $"{this.Mutation.Name}={(isSeen ? "true" : "false")}"),
        { DesiredFlaggedState: { } isFlagged } => string.Create(
            CultureInfo.InvariantCulture,
            $"{this.Mutation.Name}={(isFlagged ? "true" : "false")}"),
        { Keywords: { } keywords } => string.Create(
            CultureInfo.InvariantCulture,
            $"{this.Mutation.Name}={string.Join(KeywordSeparator, keywords.Values)}"),
        _ => this.Mutation.Name,
    };

    /// <summary>Asks for the matching email to be moved into another folder.</summary>
    /// <param name="destination">How the folder to move it into is named.</param>
    /// <returns>The action.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="destination" /> is the unspecified struct default.</exception>
    public static MailRuleAction Relocate(MailFolderReference destination) => new(
        MailboxMutation.Relocate,
        SpecifiedDestination(destination),
        desiredSeenState: null,
        desiredFlaggedState: null,
        keywords: null);

    /// <summary>Asks for a second live occurrence of the matching email to be put into another folder.</summary>
    /// <param name="destination">How the folder to copy it into is named.</param>
    /// <returns>The action.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="destination" /> is the unspecified struct default.</exception>
    public static MailRuleAction Copy(MailFolderReference destination) => new(
        MailboxMutation.Copy,
        SpecifiedDestination(destination),
        desiredSeenState: null,
        desiredFlaggedState: null,
        keywords: null);

    /// <summary>Asks for the matching email to be removed from the folder it is in.</summary>
    /// <returns>The action.</returns>
    public static MailRuleAction Delete() => new(
        MailboxMutation.Delete,
        destination: null,
        desiredSeenState: null,
        desiredFlaggedState: null,
        keywords: null);

    /// <summary>Asks for the remote <c>\Seen</c> flag of the matching email to be set or cleared.</summary>
    /// <param name="isSeen"><see langword="true" /> to mark the email read; <see langword="false" /> to mark it unread.</param>
    /// <returns>The action.</returns>
    public static MailRuleAction SetSeen(bool isSeen) => new(
        MailboxMutation.SetSeen,
        destination: null,
        isSeen,
        desiredFlaggedState: null,
        keywords: null);

    /// <summary>Asks for the remote <c>\Flagged</c> flag of the matching email to be set or cleared.</summary>
    /// <param name="isFlagged"><see langword="true" /> to flag the email; <see langword="false" /> to clear the flag.</param>
    /// <returns>The action.</returns>
    public static MailRuleAction SetFlagged(bool isFlagged) => new(
        MailboxMutation.SetFlagged,
        destination: null,
        desiredSeenState: null,
        isFlagged,
        keywords: null);

    /// <summary>Asks for keywords to be put on the matching email, beside the ones it already carries.</summary>
    /// <param name="keywords">The keywords to put on it.</param>
    /// <returns>The action.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keywords" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keywords" /> names none.</exception>
    public static MailRuleAction AddKeywords(AuthoredMailKeywords keywords) => new(
        MailboxMutation.AddKeywords,
        destination: null,
        desiredSeenState: null,
        desiredFlaggedState: null,
        NamedKeywords(keywords, MailboxMutation.AddKeywords));

    /// <summary>Asks for keywords to be taken off the matching email, leaving the ones it is not asked about.</summary>
    /// <param name="keywords">The keywords to take off it.</param>
    /// <returns>The action.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keywords" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keywords" /> names none.</exception>
    public static MailRuleAction RemoveKeywords(AuthoredMailKeywords keywords) => new(
        MailboxMutation.RemoveKeywords,
        destination: null,
        desiredSeenState: null,
        desiredFlaggedState: null,
        NamedKeywords(keywords, MailboxMutation.RemoveKeywords));

    /// <summary>Asks for the matching email's keywords to become exactly the set that was named.</summary>
    /// <param name="keywords">The keywords it should end up carrying, which may be none and then clears them all.</param>
    /// <returns>The action.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keywords" /> is <see langword="null" />.</exception>
    public static MailRuleAction SetKeywords(AuthoredMailKeywords keywords)
    {
        ArgumentNullException.ThrowIfNull(keywords);

        return new MailRuleAction(
            MailboxMutation.SetKeywords,
            destination: null,
            desiredSeenState: null,
            desiredFlaggedState: null,
            keywords);
    }

    /// <summary>Refuses an empty keyword set for the two actions that would then ask for nothing.</summary>
    /// <remarks>
    /// The replacement is exempt and reaches this method not at all: naming no keyword is how a rule asks for every
    /// keyword to be cleared, which is the one thing the other two cannot say however many keywords they name.
    /// </remarks>
    private static AuthoredMailKeywords NamedKeywords(AuthoredMailKeywords keywords, MailboxMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(keywords);

        return keywords.IsEmpty
            ? throw new ArgumentException($"A '{mutation.Name}' action must name at least one keyword.", nameof(keywords))
            : keywords;
    }

    /// <summary>Refuses a destination that names no folder, so an action carrying one cannot be constructed.</summary>
    private static MailFolderReference SpecifiedDestination(MailFolderReference destination) => destination.IsSpecified
        ? destination
        : throw new ArgumentException(
            "A relocation or a copy must name the folder it files into.",
            nameof(destination));
}
