// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>The failure raised when a question arrives at a deployment that cannot answer it.</summary>
/// <remarks>
/// <para>
/// The tool this failure belongs to is absent from what a deployment advertises while it holds, so a caller ordinarily
/// never meets it. What it covers is the gap between the two: a client that read the tool list a moment before the
/// provider stopped answering, one that remembers a list from an earlier session, and one that calls a tool it was
/// never offered.
/// </para>
/// <para>
/// It says which of the two states the deployment is in and nothing else. Neither message names an endpoint, a model, a
/// credential, or a provider's answer, because the caller cannot act on any of them and the operator reads them from the
/// health record instead.
/// </para>
/// </remarks>
public sealed class MailAnsweringUnavailableException : MailFathomException
{
    private MailAnsweringUnavailableException(string operatorSafeMessage, MailAnsweringAvailability availability)
        : base(operatorSafeMessage) => this.Availability = availability;

    /// <summary>Gets what the deployment could do when the question arrived.</summary>
    public MailAnsweringAvailability Availability { get; }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailAnsweringUnavailable;

    /// <summary>Refuses a question on a deployment that answers none.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>Asking again buys the same answer until an operator configures a chat endpoint and activates an embedding profile, so the message says the capability is absent rather than delayed.</remarks>
    public static MailAnsweringUnavailableException NotServed() => new(
        "This deployment does not answer questions about mail.",
        MailAnsweringAvailability.Inactive);

    /// <summary>Refuses a question on a deployment that answers them and currently cannot.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>Nothing about the request caused it, so the message says so: a caller that rewrites the question reaches the same refusal, and recovery is the operator's and needs no restart.</remarks>
    public static MailAnsweringUnavailableException TemporarilyUnable() => new(
        "This deployment answers questions about mail and currently cannot. Nothing about the request caused it.",
        MailAnsweringAvailability.Degraded);
}
