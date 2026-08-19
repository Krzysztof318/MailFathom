// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Filing;

/// <summary>The flags a message carries into the folder MailFathom appends it to.</summary>
/// <param name="IsDraft">Whether the copy is marked <c>\Draft</c>, which says the message has not left yet.</param>
/// <param name="IsSeen">Whether the copy is marked <c>\Seen</c>, which says nobody has to read it.</param>
/// <remarks>
/// <para>
/// Two flags and no more, because those are the two an appended copy of the owner's own message can honestly carry.
/// <c>\Draft</c> states that the message is still being composed or is still waiting to go out, and <c>\Seen</c> states
/// that the owner need not read what they wrote themselves — a sent copy arriving unread would put an unread count on
/// the owner's own outgoing mail in every client they open.
/// </para>
/// <para>
/// The set is closed rather than a general flag bag, for the reason
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
/// keeps the mutations closed: a flag MailFathom writes is an assertion it has to be able to stand behind, and these
/// two are the ones the filing itself establishes. Nothing else is expressible, so no caller can widen it.
/// </para>
/// <para>
/// This says nothing about a message already in a mailbox. It is the initial flag set of a message being created by an
/// <c>APPEND</c>, which is a different act from the <c>STORE</c> that changes a message somebody already has.
/// </para>
/// </remarks>
public readonly record struct AppendedMailFlags(bool IsDraft, bool IsSeen)
{
    /// <summary>Gets the flag set of a copy that carries neither flag.</summary>
    public static AppendedMailFlags None { get; }

    /// <summary>Gets the flag set of a message that has not left the deployment yet.</summary>
    public static AppendedMailFlags Draft { get; } = new(IsDraft: true, IsSeen: false);

    /// <summary>Gets the flag set of a copy of a message that has already been delivered.</summary>
    public static AppendedMailFlags Seen { get; } = new(IsDraft: false, IsSeen: true);
}
