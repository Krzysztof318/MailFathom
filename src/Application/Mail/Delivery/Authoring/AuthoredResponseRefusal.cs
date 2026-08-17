// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Authoring;

/// <summary>States why no answer was authored, in terms that carry nothing of the message it was about.</summary>
/// <param name="Reason">What stopped it.</param>
/// <param name="Bound">The number that was exceeded, when the reason is a bound; otherwise <see langword="null" />.</param>
/// <remarks>
/// The pair is the whole of what a refusal may carry. Everything an authoring attempt knows besides them — the
/// addresses it resolved, the subject it would have written, the text it would have quoted, the files it would have
/// carried — is the correspondence of the people the message is between, so a refusal that named any of it would put
/// mail content into every log line and exception that travelled with it.
/// </remarks>
public sealed record AuthoredResponseRefusal(AuthoredResponseRefusalReason Reason, long? Bound = null)
{
    /// <summary>Gets the published identity of this refusal.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the reason is not one this system declares, which a refusal built from a cast integer is.</exception>
    public MailFathomErrorCode Failure => this.Reason switch
    {
        AuthoredResponseRefusalReason.AnsweredEmailNotFound => MailFathomErrorCode.AnsweredEmailNotFound,
        AuthoredResponseRefusalReason.AnsweredEmailContentUnavailable =>
            MailFathomErrorCode.AnsweredEmailContentUnavailable,
        AuthoredResponseRefusalReason.SenderUnconfigured => MailFathomErrorCode.OutgoingEmailSenderUnconfigured,
        AuthoredResponseRefusalReason.BoundExceeded => MailFathomErrorCode.OutgoingEmailBoundExceeded,
        _ => throw new InvalidOperationException("The authoring refusal reason is not one this system declares."),
    };
}
