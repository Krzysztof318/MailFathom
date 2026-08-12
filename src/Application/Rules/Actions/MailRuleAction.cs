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
/// mutation is the parameter the rule declared: a relocation and a copy name a destination alias, a <c>\Seen</c> change
/// names a direction, and a delete names neither.
/// </para>
/// <para>
/// The destination is an alias rather than a remote path, because an alias is MailFathom's own name for a folder and
/// survives the server renaming what it is bound to. The path is resolved when the request is written, which is what
/// makes a destination that has stopped being resolvable fail visibly instead of being filed somewhere else.
/// </para>
/// <para>
/// The factories are the only way to build one, so an action carrying a parameter its mutation does not take cannot be
/// constructed — the same invariant <see cref="MailboxMutationRequest" /> holds one layer down.
/// </para>
/// </remarks>
public sealed record MailRuleAction
{
    private MailRuleAction(MailboxMutation mutation, MailFolderAlias? destinationAlias, bool? desiredSeenState)
    {
        this.Mutation = mutation;
        this.DestinationAlias = destinationAlias;
        this.DesiredSeenState = desiredSeenState;
    }

    /// <summary>Gets the change this action asks for.</summary>
    public MailboxMutation Mutation { get; }

    /// <summary>Gets the folder a relocation or a copy names, and <see langword="null" /> for every other action.</summary>
    public MailFolderAlias? DestinationAlias { get; }

    /// <summary>Gets which way a <c>\Seen</c> change was asked for, and <see langword="null" /> for every other action.</summary>
    public bool? DesiredSeenState { get; }

    /// <summary>Gets the form this action is rendered in for a rule set's derived revision identity.</summary>
    /// <remarks>
    /// It names the mutation and its parameter, so editing either moves the revision and the edited rule asks afresh
    /// rather than being read as the request already performed.
    /// </remarks>
    public string CanonicalForm => this switch
    {
        { DestinationAlias: { } alias } => string.Create(
            CultureInfo.InvariantCulture,
            $"{this.Mutation.Name}={alias.Value}"),
        { DesiredSeenState: { } isSeen } => string.Create(
            CultureInfo.InvariantCulture,
            $"{this.Mutation.Name}={(isSeen ? "true" : "false")}"),
        _ => this.Mutation.Name,
    };

    /// <summary>Asks for the matching email to be moved into another folder.</summary>
    /// <param name="destinationAlias">The folder to move it into.</param>
    /// <returns>The action.</returns>
    public static MailRuleAction Relocate(MailFolderAlias destinationAlias) =>
        new(MailboxMutation.Relocate, destinationAlias, desiredSeenState: null);

    /// <summary>Asks for a second live occurrence of the matching email to be put into another folder.</summary>
    /// <param name="destinationAlias">The folder to copy it into.</param>
    /// <returns>The action.</returns>
    public static MailRuleAction Copy(MailFolderAlias destinationAlias) =>
        new(MailboxMutation.Copy, destinationAlias, desiredSeenState: null);

    /// <summary>Asks for the matching email to be removed from the folder it is in.</summary>
    /// <returns>The action.</returns>
    public static MailRuleAction Delete() => new(MailboxMutation.Delete, destinationAlias: null, desiredSeenState: null);

    /// <summary>Asks for the remote <c>\Seen</c> flag of the matching email to be set or cleared.</summary>
    /// <param name="isSeen"><see langword="true" /> to mark the email read; <see langword="false" /> to mark it unread.</param>
    /// <returns>The action.</returns>
    public static MailRuleAction SetSeen(bool isSeen) => new(MailboxMutation.SetSeen, destinationAlias: null, isSeen);
}
