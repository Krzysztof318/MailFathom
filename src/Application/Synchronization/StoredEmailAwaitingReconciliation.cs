// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Synchronization;

/// <summary>One locally stored occurrence a reconciliation window has selected to ask the server about.</summary>
/// <param name="StoredEmailId">The stable local identity the outcome is written against.</param>
/// <param name="Uid">The UID the server is asked about, within the folder and UIDVALIDITY the window was opened for.</param>
/// <remarks>
/// The pair is everything reconciliation needs and deliberately nothing more. No subject, address, or fragment of a
/// message takes part in deciding whether an email still exists remotely, so none of it is read to decide it.
/// </remarks>
public sealed record StoredEmailAwaitingReconciliation(StoredEmailId StoredEmailId, ImapUid Uid);
