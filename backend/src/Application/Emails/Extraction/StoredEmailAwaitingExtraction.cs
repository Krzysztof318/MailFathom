// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Extraction;

/// <summary>Names one locally stored email whose raw MIME has never been read for metadata and text.</summary>
/// <param name="StoredEmailId">The stable local identity, which is also the backfill's resume position.</param>
/// <param name="Account">The user and the account the message belongs to, whose posture decides what is redacted out of its body.</param>
/// <remarks>
/// <para>
/// Nothing about where a mail server holds the message travels with the row, because extraction reads the stored copy:
/// a message no server holds any longer is read exactly as one it still does.
/// </para>
/// <para>
/// The account travels with it for the reason the walk is one walk over everybody's mail: two rows in one batch can
/// belong to users with different scanning postures, so the answer has to be a property of the row rather than of the
/// pass.
/// </para>
/// </remarks>
public sealed record StoredEmailAwaitingExtraction(
    StoredEmailId StoredEmailId,
    MailAccountId Account);
