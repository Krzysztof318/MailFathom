// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Signals;

/// <summary>Carries a signal to whatever an owner is reachable through.</summary>
/// <remarks>
/// <para>
/// One implementation is registered today, and it is SignalR: a hub reaches a client that is running, which is the web
/// head, the desktop head, and the Android head while it is in the foreground. The port exists because that is not the
/// whole of the delivery problem — an Android application that has been closed is reachable only through Firebase Cloud
/// Messaging, which is a different medium rather than a second transport, and designing it as a second channel over
/// these same signals is what keeps it from becoming a second vocabulary for the same events.
/// </para>
/// <para>
/// A channel never fails a caller. Everything that raises a signal has already committed the work the signal describes,
/// so a channel that could not deliver logs and returns; the client's own interval is what closes the gap, and a
/// deployment whose channel is down behaves exactly as one that never had it.
/// </para>
/// </remarks>
public interface IClientSignalChannel
{
    /// <summary>Delivers one signal to the owner it names, and to no other owner.</summary>
    /// <param name="signal">What changed and for whom.</param>
    /// <param name="cancellationToken">Cancels the delivery when the process is stopping.</param>
    /// <returns>A task that completes when the channel has done what it can with the signal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signal" /> is <see langword="null" />.</exception>
    Task PublishAsync(ClientSignal signal, CancellationToken cancellationToken);
}
