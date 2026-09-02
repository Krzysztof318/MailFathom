// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailKit.Net.Smtp;

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>An SMTP client that reports what the server answered about each address of the envelope it is offering.</summary>
/// <remarks>
/// <para>
/// It is the mail library's own client contract with one addition, because the library reports per-recipient replies
/// through callbacks on the client rather than in the result of a submission — and a partial acceptance is exactly what
/// a durable outbox has to record. Extending the contract rather than reaching for the concrete client is what keeps a
/// unit test able to script a submission server without a socket.
/// </para>
/// <para>
/// The observer belongs to one submission. It is set before the message is offered and cleared afterwards, so a client
/// between submissions reports to nobody.
/// </para>
/// </remarks>
internal interface ISubmissionClient : ISmtpClient
{
    /// <summary>Gets or sets what the current submission's envelope replies are written into, or <see langword="null" /> between submissions.</summary>
    SmtpEnvelopeObserver? Envelope { get; set; }
}
