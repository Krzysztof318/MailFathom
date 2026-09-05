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

    /// <summary>Gets the kind raised when flags moved, mail was moved or deleted remotely, or a pending change settled.</summary>
    public static ClientSignalKind MailChanged { get; } = new("mail.changed");

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
