// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>The relational envelope of one owner this deployment holds a record for.</summary>
/// <param name="Owner">The identity every mail account and every stored message of theirs hangs on.</param>
/// <param name="DisplayName">The label an operator tells this owner apart by, which is unique across the deployment.</param>
/// <param name="DocumentWrittenAtRuntime">Whether anything has written this owner's document while the deployment was running.</param>
/// <remarks>
/// The document itself is deliberately absent. What a start decides about an owner — whether they are served, which
/// source supplies their mail accounts, and whether a declaration may still reach them — is decided from the envelope
/// alone, so establishing the roster never materializes one person's record, let alone everybody's.
/// <para>
/// The marker is what tells an owner whose document is empty because nothing has filled it from one whose owner emptied
/// it on purpose. The first is read from configuration and the second from their own document, and the two are
/// indistinguishable in the column beside it.
/// </para>
/// </remarks>
public sealed record MailOwnerRecord(MailOwnerId Owner, string DisplayName, bool DocumentWrittenAtRuntime)
{
    /// <summary>The longest label an owner is told apart by.</summary>
    /// <remarks>
    /// Stated here rather than on the row it bounds because two things have to agree about it and neither owns the
    /// other: the column that stores the label, and the rule that judges a declaration carrying one. A declaration
    /// refused for a label the column would have truncated is a refusal an operator can act on; the same declaration
    /// accepted and then refused by PostgreSQL is a start that fails with the server's own sentence.
    /// </remarks>
    public const int MaximumDisplayNameLength = 128;
}
