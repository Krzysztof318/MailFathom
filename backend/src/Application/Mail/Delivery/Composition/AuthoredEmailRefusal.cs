// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>States why a message was not composed, in the terms the author can act on.</summary>
/// <param name="Reason">What was wrong.</param>
/// <param name="Field">The part of the authored message it was wrong in.</param>
/// <param name="Bound">The number that was exceeded, when the reason is a bound; otherwise <see langword="null" />.</param>
/// <remarks>
/// The three together are the whole of what a refusal may carry. Everything else a composition knows — the address, the
/// subject, the body, the file — is mail content, so a refusal names the field and the limit and nothing that was in
/// it. The bound is the configured or advertised number rather than what the message measured, for the same reason: a
/// measurement of somebody's attachment says how large their file was.
/// </remarks>
public sealed record AuthoredEmailRefusal(
    AuthoredEmailRefusalReason Reason,
    AuthoredEmailField Field,
    long? Bound = null)
{
    /// <summary>Gets the published identity of this refusal.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the reason is not one this system declares, which a refusal built from a cast integer is.</exception>
    public MailFathomErrorCode Failure => this.Reason switch
    {
        AuthoredEmailRefusalReason.SenderUnconfigured => MailFathomErrorCode.OutgoingEmailSenderUnconfigured,
        AuthoredEmailRefusalReason.HeaderInjected => MailFathomErrorCode.OutgoingEmailHeaderInjected,
        AuthoredEmailRefusalReason.FieldUnusable => MailFathomErrorCode.OutgoingEmailFieldUnusable,
        AuthoredEmailRefusalReason.InternationalizationUnsupported =>
            MailFathomErrorCode.OutgoingEmailInternationalizationUnsupported,
        AuthoredEmailRefusalReason.BoundExceeded => MailFathomErrorCode.OutgoingEmailBoundExceeded,
        _ => throw new InvalidOperationException(
            "The composition refusal reason is not one this system declares."),
    };
}
