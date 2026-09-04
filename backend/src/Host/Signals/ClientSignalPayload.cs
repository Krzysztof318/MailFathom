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
/// <param name="NotificationKind">Which kind of notification was written, where the kind reports one.</param>
/// <param name="Headline">The notification's own headline, and nothing for every other kind.</param>
/// <param name="SecondLine">The notification's own second line, and nothing for every other kind.</param>
/// <remarks>
/// <para>
/// <b>It names no owner.</b> The connection already belongs to one — it joined that owner's group and nothing else can
/// reach it — so writing the identifier into every message would put a value the client has no use for onto the wire
/// on every change.
/// </para>
/// <para>
/// <b>No mail crosses.</b> The vocabulary is the one <see cref="ClientSignal" /> holds and nothing widens it here: a
/// count, an account alias, a folder alias, and a stored identity, plus the notification record's own already-derived
/// two lines, which are the stated exception and reach a client entitled to read that record over its own route.
/// </para>
/// </remarks>
internal sealed record ClientSignalPayload(
    string Kind,
    string? Account,
    string? Folder,
    int Count,
    IReadOnlyList<string> Emails,
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
            signal.NotificationKind?.ToString(),
            signal.Headline,
            signal.SecondLine);
    }
}
