// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MimeKit;

namespace MailFathom.SyntheticMail.Delivery;

/// <summary>The mailbox MailFathom synchronizes, narrowed to the two things an exchange does to it.</summary>
/// <remarks>
/// <para>
/// This is deliberately not a restatement of MailKit's own interface, for the reason
/// <see cref="ISyntheticMailTransport" /> is not one of the submission client's. It names one mailbox's whole session,
/// opens it once, and carries behavior of its own: reading the identifier a server assigned without ever asking for
/// anything that could set <c>\Seen</c>, resolving which folder a mailbox keeps its own mail in, and translating the
/// library's failures into <see cref="SyntheticMailFailure" /> so nothing above it has to know which library refused.
/// </para>
/// <para>
/// <see cref="FindDeliveredMessageIdAsync" /> answers once and does not wait. Waiting is the caller's, because how
/// long a run is willing to wait for a delivery, how often it looks, and what it says when the wait runs out all
/// belong with the report rather than with the connection — and a port that slept would need a clock of its own to be
/// testable at all.
/// </para>
/// </remarks>
internal interface IWatchedMailbox : IAsyncDisposable
{
    /// <summary>Opens the session, authenticates it over a secured connection, and resolves the folders it will use.</summary>
    /// <param name="cancellationToken">Cancels the connection and the authentication.</param>
    /// <returns>A task that completes once the mailbox is ready to be read and appended to.</returns>
    /// <exception cref="SyntheticMailFailure">Thrown when the endpoint cannot be reached, cannot be secured, refuses the credential, or advertises no folder to file sent mail in.</exception>
    Task OpenAsync(CancellationToken cancellationToken);

    /// <summary>Looks once for a delivered message carrying one marker, and reports the identifier its server assigned.</summary>
    /// <param name="marker">The value the submission was stamped with, as <see cref="SyntheticDeliveryMarker" /> describes.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The delivered copy's <c>Message-Id</c> without angle brackets, or <see langword="null" /> when nothing carrying that marker has arrived yet.</returns>
    /// <exception cref="SyntheticMailFailure">Thrown when the server refuses the search or the connection fails.</exception>
    Task<string?> FindDeliveredMessageIdAsync(string marker, CancellationToken cancellationToken);

    /// <summary>Files a message the mailbox itself wrote, where a mail client would have filed it.</summary>
    /// <param name="message">The composed message, which the caller still owns and disposes.</param>
    /// <param name="cancellationToken">Cancels the append.</param>
    /// <returns>A task that completes once the server has stored the copy.</returns>
    /// <exception cref="SyntheticMailFailure">Thrown when the server refuses the append or the connection fails.</exception>
    Task AppendToSentAsync(MimeMessage message, CancellationToken cancellationToken);
}
