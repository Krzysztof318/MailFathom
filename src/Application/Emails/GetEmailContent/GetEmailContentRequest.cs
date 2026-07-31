// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Domain.Emails;

namespace MailMcp.Application.Emails.GetEmailContent;

/// <summary>What a caller asks for when reading one email from the local mailbox copy.</summary>
/// <param name="StoredEmailId">The stable local identity a listing returned for the email.</param>
/// <param name="IncludeSanitizedHtml">Whether to also return the sanitized HTML representation of the body.</param>
/// <remarks>
/// The email is named by its domain identity rather than as text, so an adapter converts a caller's string once, at its
/// own boundary, and a malformed identifier is refused before it reaches a use case.
/// <para>
/// The HTML representation is opt-in because it costs a sanitization pass over untrusted markup and because plain text
/// is what most callers want: a model reading mail is better served by the words than by the layout around them.
/// </para>
/// </remarks>
public sealed record GetEmailContentRequest(StoredEmailId StoredEmailId, bool IncludeSanitizedHtml = false);
