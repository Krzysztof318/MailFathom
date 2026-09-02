// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Delivery;
using MimeKit;

namespace MailFathom.SyntheticMail.UnitTests.TestDoubles;

/// <summary>A submission session that accepts everything it is given, or refuses whatever a test tells it to.</summary>
/// <remarks>
/// Hand-written rather than substituted, because what has to be recorded is the message's <em>state at send time</em>:
/// the batch owns each message and disposes it as soon as the call returns, so a captured argument would be read after
/// disposal. <c>FakeHttpMessageHandler</c> is hand-written in this repository for the same reason.
/// </remarks>
internal sealed class RecordingSyntheticMailTransport : ISyntheticMailTransport
{
    private readonly Func<MimeMessage, string?> refusal;

    /// <summary>Initializes a session that accepts everything.</summary>
    internal RecordingSyntheticMailTransport()
        : this(_ => null)
    {
    }

    /// <summary>Initializes a session that refuses the messages a rule selects.</summary>
    /// <param name="refusal">Answers with the reason to refuse a message, or <see langword="null" /> to accept it.</param>
    internal RecordingSyntheticMailTransport(Func<MimeMessage, string?> refusal) => this.refusal = refusal;

    /// <summary>Every submission the batch made, in order.</summary>
    internal List<SubmittedMessage> Submissions { get; } = [];

    /// <summary>How many times the session was opened.</summary>
    internal int Opened { get; private set; }

    /// <summary>Whether the session was disposed.</summary>
    internal bool Disposed { get; private set; }

    /// <inheritdoc />
    public Task OpenAsync(CancellationToken cancellationToken)
    {
        this.Opened++;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendAsync(MimeMessage message, MailboxAddress recipient, CancellationToken cancellationToken)
    {
        this.Submissions.Add(Snapshot(message, recipient));

        return this.refusal(message) is { } reason
            ? Task.FromException(new SyntheticMailFailure(reason))
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        this.Disposed = true;

        return ValueTask.CompletedTask;
    }

    private static SubmittedMessage Snapshot(MimeMessage message, MailboxAddress recipient) => new(
        message.MessageId ?? string.Empty,
        message.Subject ?? string.Empty,
        [recipient.Address],
        Addresses(message.From),
        message.Sender?.Address,
        Addresses(message.ReplyTo),
        Addresses(message.To),
        Addresses(message.Cc),
        message.InReplyTo,
        [.. message.References],
        message.Headers[SyntheticDeliveryMarker.HeaderName]);

    private static IReadOnlyList<string> Addresses(InternetAddressList addresses) =>
        [.. addresses.OfType<MailboxAddress>().Select(address => address.Address)];
}
