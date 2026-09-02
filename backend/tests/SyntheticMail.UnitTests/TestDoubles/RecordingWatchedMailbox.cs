// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Delivery;
using MimeKit;

namespace MailFathom.SyntheticMail.UnitTests.TestDoubles;

/// <summary>A watched mailbox that answers whatever a test says its server assigned, and records what was filed in it.</summary>
/// <remarks>
/// <para>
/// Hand-written rather than substituted, for the reason <see cref="RecordingSyntheticMailTransport" /> is: the
/// delivery owns each composed message and disposes it as soon as the call returns, so a captured argument would be
/// read after disposal.
/// </para>
/// <para>
/// Its default answer rewrites the identifier it was handed, which is the behaviour the whole mode exists for. A
/// double that echoed the proposed value back would pass every assertion about threading while proving nothing about
/// where the identifier came from.
/// </para>
/// </remarks>
internal sealed class RecordingWatchedMailbox : IWatchedMailbox
{
    /// <summary>The prefix the default answer puts in front of a proposed identifier.</summary>
    internal const string AssignedPrefix = "assigned.";

    private readonly Func<string, string?> assignment;

    /// <summary>Initializes a mailbox whose server rewrites every identifier it is given.</summary>
    internal RecordingWatchedMailbox()
        : this(marker => AssignedPrefix + marker)
    {
    }

    /// <summary>Initializes a mailbox that answers with whatever a rule decides.</summary>
    /// <param name="assignment">Answers with the identifier the delivered copy carries, or <see langword="null" /> when nothing carrying that marker has arrived.</param>
    internal RecordingWatchedMailbox(Func<string, string?> assignment) => this.assignment = assignment;

    /// <summary>Every message filed in the Sent folder, in order.</summary>
    internal List<SubmittedMessage> Appended { get; } = [];

    /// <summary>Every marker the delivery looked for, in order, including repeated looks.</summary>
    internal List<string> Searches { get; } = [];

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
    public Task<string?> FindDeliveredMessageIdAsync(string marker, CancellationToken cancellationToken)
    {
        this.Searches.Add(marker);

        return Task.FromResult(this.assignment(marker));
    }

    /// <inheritdoc />
    public Task AppendToSentAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        this.Appended.Add(Snapshot(message));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        this.Disposed = true;

        return ValueTask.CompletedTask;
    }

    private static SubmittedMessage Snapshot(MimeMessage message) => new(
        message.MessageId ?? string.Empty,
        message.Subject ?? string.Empty,
        [],
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
