// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>States what happened when a copy of a message MailFathom composed was appended to a folder.</summary>
/// <remarks>
/// The three refusals are separate because what a caller may do next differs: two of them happened before the command
/// went out and leave the folder exactly as it was, while the third happened after it and leaves nobody able to say
/// whether the copy is there. Reporting them as one reason would let a caller retry the one append that must never be
/// repeated.
/// </remarks>
public enum MailboxCopyAppendOutcome
{
    /// <summary>The server accepted the copy and answered for it.</summary>
    Appended = 0,

    /// <summary>No folder of the account plays the role the filing names, so there is nowhere to append to.</summary>
    DestinationUnavailable = 1,

    /// <summary>Nothing is stored under the source, so there are no bytes to append.</summary>
    MessageUnavailable = 2,

    /// <summary>The append was issued and how it ended is not knowable, so the copy may or may not be in the folder.</summary>
    OutcomeUnknown = 3,
}

/// <summary>Carries what the server said about an appended copy, or the reason nothing was appended.</summary>
/// <remarks>
/// A failure is a result rather than an exception for the reason the whole filing mechanism is one: a copy that could
/// not be filed says nothing about the send or the draft it belongs to, and raising would end the pass that was
/// settling the messages beside it.
/// </remarks>
public sealed record MailboxCopyAppendResult
{
    private MailboxCopyAppendResult(
        MailboxCopyAppendOutcome outcome,
        AppendedMailCopy? copy,
        MailFathomErrorCode? failure)
    {
        this.Outcome = outcome;
        this.Copy = copy;
        this.Failure = failure;
    }

    /// <summary>Gets what happened.</summary>
    public MailboxCopyAppendOutcome Outcome { get; }

    /// <summary>Gets what the server said about the copy, which is present exactly when the append was answered.</summary>
    public AppendedMailCopy? Copy { get; }

    /// <summary>Gets the code standing for why nothing was filed, which is absent exactly when the copy was.</summary>
    public MailFathomErrorCode? Failure { get; }

    /// <summary>Reports a copy the server accepted.</summary>
    /// <param name="copy">What the server said about it.</param>
    /// <returns>An appended result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="copy" /> is <see langword="null" />.</exception>
    public static MailboxCopyAppendResult Appended(AppendedMailCopy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);

        return new MailboxCopyAppendResult(MailboxCopyAppendOutcome.Appended, copy, failure: null);
    }

    /// <summary>Reports a role no folder of the account plays.</summary>
    /// <returns>A result naming a destination that is not available.</returns>
    public static MailboxCopyAppendResult DestinationUnavailable() => new(
        MailboxCopyAppendOutcome.DestinationUnavailable,
        copy: null,
        MailFathomErrorCode.OutgoingEmailFilingDestinationUnavailable);

    /// <summary>Reports a source nothing is stored under.</summary>
    /// <returns>A result naming a message that cannot be appended.</returns>
    public static MailboxCopyAppendResult MessageUnavailable() => new(
        MailboxCopyAppendOutcome.MessageUnavailable,
        copy: null,
        MailFathomErrorCode.OutgoingEmailFilingFailedUnexpectedly);

    /// <summary>Reports an append that was issued and never settled.</summary>
    /// <param name="failure">The code standing for whatever ended it.</param>
    /// <returns>A result nobody can settle.</returns>
    public static MailboxCopyAppendResult OutcomeUnknown(MailFathomErrorCode failure) =>
        new(MailboxCopyAppendOutcome.OutcomeUnknown, copy: null, failure);
}
