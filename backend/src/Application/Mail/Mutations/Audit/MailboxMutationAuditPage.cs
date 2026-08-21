// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Mutations.Audit;

namespace MailFathom.Application.Mail.Mutations.Audit;

/// <summary>Carries one bounded page of an account's audit trail and the boundary the next page continues from.</summary>
/// <param name="Entries">The entries, newest first.</param>
/// <param name="NextCursor">The cursor a caller presents for the following page, or <see langword="null" /> when this page reached the end of the trail.</param>
/// <remarks>
/// The absent cursor is the end of the walk rather than a page that happened to be short: a page is only ever short
/// because the filtered trail held nothing more, so a caller stops when the cursor stops instead of comparing the count
/// against the size it asked for.
/// </remarks>
public sealed record MailboxMutationAuditPage(
    IReadOnlyList<MailboxMutationAuditEntry> Entries,
    MailboxMutationAuditCursor? NextCursor);
