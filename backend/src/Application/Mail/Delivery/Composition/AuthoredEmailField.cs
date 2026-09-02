// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>Names the part of an authored message a refusal is about.</summary>
/// <remarks>
/// It is what a refusal says instead of what was wrong with the value. An address, a subject, and a body are personal
/// data of the people a message is between, so a refusal that quoted the offending value would put mail content into
/// every log line, metric, and exception that carried it; naming the field tells the author where to look and tells
/// everything downstream nothing it should not hold.
/// </remarks>
public enum AuthoredEmailField
{
    /// <summary>The people the message is addressed to, taken together across the three headers.</summary>
    Recipients = 0,

    /// <summary>The <c>To</c> header.</summary>
    To = 1,

    /// <summary>The <c>Cc</c> header.</summary>
    Cc = 2,

    /// <summary>The <c>Bcc</c> header.</summary>
    Bcc = 3,

    /// <summary>The subject line.</summary>
    Subject = 4,

    /// <summary>The plain-text body.</summary>
    PlainTextBody = 5,

    /// <summary>The HTML alternative.</summary>
    HtmlBody = 6,

    /// <summary>An attached file, by its name, its media type, or its octets.</summary>
    Attachment = 7,

    /// <summary>The address the message would be sent from, which the sending account configures.</summary>
    Sender = 8,

    /// <summary>The composed message as a whole, rather than any one field of it.</summary>
    Message = 9,
}
