// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes how far a queued message has got, in words a caller cannot misread as a delivery.</summary>
/// <remarks>
/// <para>
/// The published spellings are not the stored stage names, and the difference is the point. A record opens at
/// <c>Recorded</c>, which is an accurate name for a row and a dangerous one for a reader: an agent told a message was
/// recorded will report that it was sent. <c>queued</c> says the one thing that is true of every message this tool
/// answers about — it will leave, and it has not left.
/// </para>
/// <para>
/// Every stage is published rather than only the one a fresh submission reaches, because an identical earlier request
/// answers with the record it already wrote, and that record may have got anywhere by now.
/// </para>
/// </remarks>
internal enum SendEmailState
{
    /// <summary>The message is written down and nothing has been offered to a mail server for it.</summary>
    [Description("The message is written down and waiting. Nothing has been offered to a mail server for it yet, and it will be sent by the next delivery pass.")]
    Queued = 0,

    /// <summary>The message has begun to go out and the server's answer to it has not been read.</summary>
    [Description("The message has begun to go out and the mail server's answer has not been read yet. Whether it was delivered is not known, and it will not be offered a second time.")]
    Sending = 1,

    /// <summary>The mail server accepted the message for every recipient it had accepted.</summary>
    [Description("The mail server accepted the message. It has left this deployment and cannot be recalled.")]
    Sent = 2,

    /// <summary>The message will not be offered again, and the failure that ended it is on the record.</summary>
    [Description("The message will not be offered again. A mail server refused it permanently, or every attempt it was allowed was spent.")]
    Refused = 3,

    /// <summary>The message was withdrawn before it was delivered, and nothing was transmitted for it.</summary>
    [Description("The message was withdrawn before it was delivered. Nothing was transmitted for it.")]
    Cancelled = 4,
}
