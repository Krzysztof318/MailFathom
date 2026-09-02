// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Emails.GetEmailContent;

/// <summary>The failure raised when a read names no email at all, or more emails than one call serves.</summary>
/// <remarks>
/// <para>
/// It refuses the request rather than truncating the list, which is the convention every bound on this surface follows:
/// a caller handed the first ten of its fifteen emails could not tell which five it did not receive, and would have to
/// compare the answer against what it asked for to find out.
/// </para>
/// <para>
/// One failure covers both ends of the range for the reason a page size does. Naming nothing and naming too much are the
/// same finding about a count the caller chose, and the message states the range rather than the value it was handed.
/// </para>
/// </remarks>
public sealed class EmailContentReadCountOutOfRangeException : MailFathomException
{
    /// <summary>Initializes the failure.</summary>
    /// <param name="maximumEmails">The greatest number of emails one read serves.</param>
    public EmailContentReadCountOutOfRangeException(int maximumEmails)
        : base(string.Format(
            CultureInfo.InvariantCulture,
            "A content read names at least one email and at most {0}.",
            maximumEmails)) => this.MaximumEmails = maximumEmails;

    /// <summary>Gets the greatest number of emails one read serves.</summary>
    public int MaximumEmails { get; }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.EmailContentReadCountOutOfRange;
}
