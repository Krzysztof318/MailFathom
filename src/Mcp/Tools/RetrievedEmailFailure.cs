// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.ComponentModel;
using MailFathom.Application.Emails.GetEmailContent;

namespace MailFathom.Mcp.Tools;

/// <summary>Publishes why one named email carries no content.</summary>
/// <remarks>
/// It is part of a successful call rather than an error result, because the call did what it was asked: it answered for
/// every email it was given. The codes are the same ones a failed call reports, so a client matches on one set of
/// numbers whether the finding was about the request or about one of the emails in it.
/// </remarks>
[Description("Why this email carries no content. Its code is the same stable five-digit code a failed call reports.")]
internal sealed record RetrievedEmailFailure
{
    /// <summary>Gets the stable code identifying the failure.</summary>
    [Description("The stable five-digit MailFathom error code: 53002 when the local mailbox copy holds no such email, and 55001 when it holds the email and cannot currently serve its stored content. Only the second is worth asking about again.")]
    public required int Code { get; init; }

    /// <summary>Gets the sentence written for whoever, or whatever, reads the result.</summary>
    [Description("The failure in words, naming the email by the identifier the call supplied and nothing else.")]
    public required string Message { get; init; }

    /// <summary>Publishes the failure a read reported for one email.</summary>
    /// <param name="failure">The failure the use case produced.</param>
    /// <returns>The wire representation of <paramref name="failure" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="failure" /> is <see langword="null" />.</exception>
    public static RetrievedEmailFailure From(EmailContentReadFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new RetrievedEmailFailure
        {
            Code = failure.ErrorCode.Value,
            Message = failure.Message,
        };
    }
}
