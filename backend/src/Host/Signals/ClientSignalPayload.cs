// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Signals;

namespace MailFathom.Host.Signals;

/// <summary>One signal as it crosses to a client.</summary>
/// <param name="Kind">The published name of what changed, which is what a client keys its handler by.</param>
/// <param name="Account">The account the change is in, where the kind names one.</param>
/// <param name="Folder">The folder alias the change is in, where the kind names one.</param>
/// <param name="Count">How many things the change covers: the mail one run committed, or how many notifications stand unread.</param>
/// <param name="Emails">The stored identities the change names, bounded where it names any and empty otherwise.</param>
/// <param name="Flags">Where the two server flags now stand for each email a flag change names, and empty for every other kind.</param>
/// <param name="NotificationKind">Which kind of notification was written, where the kind reports one.</param>
/// <param name="Headline">The notification's own headline, and nothing for every other kind.</param>
/// <param name="SecondLine">The notification's own second line, and nothing for every other kind.</param>
/// <remarks>
/// <para>
/// <b>It names no user.</b> The connection already belongs to one — it joined that user's group and nothing else can
/// reach it — so writing the identifier into every message would put a value the client has no use for onto the wire
/// on every change.
/// </para>
/// <para>
/// <b>No mail crosses.</b> The vocabulary is the one <see cref="ClientSignal" /> holds and nothing widens it here: a
/// count, an account alias, a folder alias, a stored identity, and the two server flags of one, plus the notification
/// record's own already-derived two lines, which are the stated exception and reach a client entitled to read that
/// record over its own route.
/// </para>
/// </remarks>
internal sealed record ClientSignalPayload(
    string Kind,
    string? Account,
    string? Folder,
    int Count,
    IReadOnlyList<string> Emails,
    IReadOnlyList<ClientSignalFlagsPayload> Flags,
    string? NotificationKind,
    string? Headline,
    string? SecondLine)
{
    /// <summary>Renders one signal for the wire.</summary>
    /// <param name="signal">What changed.</param>
    /// <returns>The payload a client is handed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signal" /> is <see langword="null" />.</exception>
    internal static ClientSignalPayload For(ClientSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        return new ClientSignalPayload(
            signal.Kind.Name,
            signal.Account?.Value,
            signal.Folder?.Value,
            signal.Count,
            [.. signal.Emails.Select(static email => email.Value.ToString())],
            [
                .. signal.Flags.Select(static flag => new ClientSignalFlagsPayload(
                    flag.Email.Value.ToString(),
                    flag.IsSeen,
                    flag.IsFlagged)),
            ],
            signal.NotificationKind?.ToString(),
            signal.Headline,
            signal.SecondLine);
    }
}

/// <summary>Where one email's two server flags now stand, as the flag change carries it.</summary>
/// <param name="Email">The stored identity, which is the same identifier every row and every route already names it by.</param>
/// <param name="IsSeen">Where the remote <c>\Seen</c> flag stands, and <see langword="null" /> where this statement is not about it.</param>
/// <param name="IsFlagged">Where the remote <c>\Flagged</c> flag stands, and <see langword="null" /> where this statement is not about it.</param>
/// <remarks>A value left out is a value the publisher did not observe rather than one that was cleared, so a client applies what is stated and leaves the rest of the row alone.</remarks>
internal sealed record ClientSignalFlagsPayload(string Email, bool? IsSeen, bool? IsFlagged);
