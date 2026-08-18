// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Outbox;

/// <summary>Reports what one pass over an account's outbox did.</summary>
/// <remarks>
/// The results are per record and in the order they were claimed, so whoever publishes the pass reports one line and
/// one measurement per send rather than a total that hides which one needs a person.
/// </remarks>
/// <param name="Results">What each claimed send ended in.</param>
/// <param name="MarkedUnknownCount">How many records this pass found stuck mid-transmission and stamped with the reason.</param>
/// <param name="BatchFilled">Whether the claim took as much as it was allowed, which means there is more waiting.</param>
public sealed record MailOutboxPassReport(
    IReadOnlyList<MailOutboxDeliveryResult> Results,
    int MarkedUnknownCount,
    bool BatchFilled)
{
    /// <summary>An account with no submission endpoint, or with nothing outstanding, produces this.</summary>
    public static MailOutboxPassReport Empty { get; } = new([], MarkedUnknownCount: 0, BatchFilled: false);

    /// <summary>Gets how many of the claimed sends the server acknowledged.</summary>
    public int SentCount => this.CountOf(MailOutboxDeliveryOutcome.Sent);

    /// <summary>Gets how many of the claimed sends nothing will offer again.</summary>
    public int RefusedCount => this.CountOf(MailOutboxDeliveryOutcome.Refused);

    /// <summary>Gets how many of the claimed sends are waiting for another attempt.</summary>
    public int DeferredCount => this.CountOf(MailOutboxDeliveryOutcome.Deferred);

    /// <summary>Gets how many of the claimed sends ended with nobody able to say what their recipients received.</summary>
    public int UnknownOutcomeCount => this.CountOf(MailOutboxDeliveryOutcome.OutcomeUnknown);

    /// <summary>Gets whether the pass met a submission server that would not serve it, which is what defers the account.</summary>
    /// <remarks>
    /// A send given back for another attempt is the shape a provider's unavailability takes here, and it is the one
    /// outcome that says something about the account rather than about the message. A refusal does not: a server that
    /// answered is a server that is working.
    /// </remarks>
    public bool AccountDeferred => this.DeferredCount > 0;

    private int CountOf(MailOutboxDeliveryOutcome outcome) =>
        this.Results.Count(result => result.Outcome == outcome);
}
