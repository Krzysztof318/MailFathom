// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Emails.Mailboxes;

/// <summary>The failure raised when a continuation cursor was issued for a different query than the one presenting it.</summary>
/// <remarks>
/// <para>
/// A keyset cursor names a position in one total order over one filtered set. Presented against a different filter or a
/// different reading direction it still names a row, which is exactly why honoring it would be wrong: the caller would
/// receive an arbitrary window of the new result set and would have no way to notice. The cursor therefore carries a
/// fingerprint of the filters it was issued for, and a mismatch is refused.
/// </para>
/// <para>
/// Changing the page size is not a mismatch. It moves no boundary, so a client is free to ask for a larger or smaller
/// page while continuing the same walk.
/// </para>
/// </remarks>
public sealed class MailboxQueryCursorFilterMismatchException : MailFathomException
{
    /// <summary>Initializes the failure for one cursor presented against filters it was not issued for.</summary>
    public MailboxQueryCursorFilterMismatchException()
        : base("The continuation cursor was issued for a different set of mailbox query filters and cannot be continued against these.")
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailboxQueryCursorFilterMismatch;
}
