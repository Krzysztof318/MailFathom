// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Signals;

/// <summary>Names one thing a deployment tells an open client has changed.</summary>
/// <remarks>
/// <para>
/// A closed enumeration rather than a C# <see langword="enum" />, because the name is the published identity: a client
/// keys its handler by it, a second delivery channel will render the same names for a person, and a numeric member
/// value would mean nothing outside this assembly. Renaming a member therefore breaks a contract loudly rather than
/// silently changing what a wire value means.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and names nothing; <see cref="IsSpecified" /> reports it,
/// and <see cref="ClientSignal" />'s factories are the only way a signal is composed, so no unspecified kind reaches a
/// channel.
/// </para>
/// </remarks>
public readonly record struct ClientSignalKind
{
    private readonly string? name;

    private ClientSignalKind(string name) => this.name = name;

    /// <summary>Gets the kind raised when a synchronization run committed mail.</summary>
    public static ClientSignalKind MailArrived { get; } = new("mail.arrived");

    /// <summary>Gets the kind raised when mail was moved or deleted remotely, keywords moved, or a pending change settled.</summary>
    public static ClientSignalKind MailChanged { get; } = new("mail.changed");

    /// <summary>Gets the kind raised when nothing about mail moved except the <c>\Seen</c> or <c>\Flagged</c> flag of one or more messages.</summary>
    /// <remarks>
    /// The one kind that states a value rather than naming somewhere to look again, which is what lets a star or a read
    /// mark land on a screen without a read behind it. It is the only change cheap enough to state: a flag is two
    /// booleans beside an identifier the client already holds, applying the same one twice is the same state, and a
    /// flag never moves a message between folders or in or out of a filtered view — so no reader has to decide whether
    /// a row still belongs where it is drawn. A change that moved anything else says <see cref="MailChanged" />.
    /// </remarks>
    public static ClientSignalKind MailFlagsChanged { get; } = new("mail.flags.changed");

    /// <summary>Gets the kind raised when the folder set itself moved.</summary>
    public static ClientSignalKind FoldersChanged { get; } = new("folders.changed");

    /// <summary>Gets the kind raised when a notification record was written.</summary>
    public static ClientSignalKind NotificationRaised { get; } = new("notification.raised");

    /// <summary>Gets the kind raised when a run finished, failed, or found the mailbox unreachable.</summary>
    public static ClientSignalKind AccountState { get; } = new("account.state");

    /// <summary>Gets every kind this deployment publishes.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<ClientSignalKind> All { get; } =
    [
        MailArrived,
        MailChanged,
        MailFlagsChanged,
        FoldersChanged,
        NotificationRaised,
        AccountState,
    ];

    /// <summary>Gets whether this value names a kind rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets the name a client keys its handler by.</summary>
    /// <exception cref="InvalidOperationException">Thrown when read from the struct default, which names no kind.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The default ClientSignalKind names no kind and has no published name.");

    /// <inheritdoc />
    public override string ToString() => this.name ?? string.Empty;
}
