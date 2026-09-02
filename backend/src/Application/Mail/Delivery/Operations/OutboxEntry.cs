// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Operations;

/// <summary>One recorded send as a listing names it: where it stands, how it got there, and what it is waiting for.</summary>
/// <remarks>
/// <para>
/// It is deliberately not <see cref="OutgoingEmailRecord" />. That record carries the addresses the message is offered
/// to, and a listing is exactly the place those must not appear: a page of an outbox is a page of who this owner writes
/// to and when, which is somebody's correspondence rather than the deployment's own state. What is left — an
/// identifier, a stage, an account alias, counts, instants, and a coded failure — is what a decision is taken from, and
/// the single-record reading is where a caller that asked about one send by identity is told who it is for.
/// </para>
/// <para>
/// The recipient count is left out for the same reason it is left off the submission span: how many people one message
/// is addressed to is a fact about that correspondence, and nothing an operator decides here depends on it.
/// </para>
/// </remarks>
public sealed record OutboxEntry
{
    /// <summary>Gets the identifier every decision names the send by.</summary>
    public required OutgoingEmailId OutgoingEmailId { get; init; }

    /// <summary>Gets the account the message is sent from, as the deployment's configuration names it.</summary>
    public required MailAccountId AccountId { get; init; }

    /// <summary>Gets how far along its submission sequence the send has durably reached.</summary>
    public required OutgoingEmailStage Stage { get; init; }

    /// <summary>Gets what asked for the send.</summary>
    public required OutgoingEmailOrigin Origin { get; init; }

    /// <summary>Gets how many attempts have been handed out for it, counting from one.</summary>
    public required int AttemptCount { get; init; }

    /// <summary>Gets how many bytes of MIME are stored for the message.</summary>
    public required long MimeByteLength { get; init; }

    /// <summary>Gets when the send was written down.</summary>
    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>Gets when it last moved between stages.</summary>
    public required DateTimeOffset StageChangedAt { get; init; }

    /// <summary>Gets the instant from which it may be claimed again, which is in the future while a backoff is running.</summary>
    public required DateTimeOffset AvailableAt { get; init; }

    /// <summary>Gets the code identifying what the last attempt ended in, or <see langword="null" /> where none is recorded.</summary>
    public required MailFathomErrorCode? LastFailure { get; init; }

    /// <summary>Gets the reply code the server answered with, or <see langword="null" /> where it answered none.</summary>
    public required int? LastReplyCode { get; init; }

    /// <summary>Gets whether nobody can say what this send's recipients received.</summary>
    /// <remarks>It is the one state that waits for a person rather than for another attempt, which is what a listing has to make visible without an operator reading each stage name.</remarks>
    public bool HasUnknownOutcome => this.Stage == OutgoingEmailStage.TransmissionBegun;
}
