// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Generation;
using MimeKit;

namespace MailFathom.SyntheticMail.Delivery;

/// <summary>One turn of an exchange, ready to be delivered, whatever produced it.</summary>
/// <param name="Author">The invented participant who wrote it, which decides who a submitted copy is <c>From</c>.</param>
/// <param name="MessageId">The identifier it proposes, which is what a failure names and what a submission is stamped with.</param>
/// <param name="Subject">Its subject, which is the other half of what a failure names.</param>
/// <param name="Compose">Builds the message, authored but not yet addressed or threaded; the caller disposes what it returns.</param>
/// <remarks>
/// <para>
/// This is the seam between where a turn came from and how it is delivered. A run that generates its own corpus builds
/// one from a <see cref="SyntheticEmail" />; a run replaying an exported corpus builds one from the message on disk.
/// Delivery is the same either way, which is the point: reading the identifier a server assigned, rewriting
/// the ancestry from it, and abandoning the rest of an exchange whose turn never arrived is the difficult part of this
/// tool, and there is one of it.
/// </para>
/// <para>
/// The message is built when its turn comes rather than held, so a batch's peak memory is one message rather than all
/// of them — which is what lets the attachment ceiling be raised without the count having to come down to match.
/// </para>
/// </remarks>
internal sealed record DeliverableTurn(
    MailboxAddress Author,
    string MessageId,
    string Subject,
    Func<MimeMessage> Compose)
{
    /// <summary>Hands one generated exchange to delivery in the shape a replayed one also arrives in.</summary>
    /// <param name="conversation">The generated exchange, oldest message first.</param>
    /// <returns>Its turns, in the same order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="conversation" /> is <see langword="null" />.</exception>
    internal static IReadOnlyList<DeliverableTurn> From(SyntheticConversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        return
        [
            .. conversation.Messages.Select(message => new DeliverableTurn(
                new MailboxAddress(message.Author.DisplayName, message.Author.Address),
                message.MessageId,
                message.Subject,
                () => SyntheticMimeComposer.ComposeAuthored(message))),
        ];
    }
}
