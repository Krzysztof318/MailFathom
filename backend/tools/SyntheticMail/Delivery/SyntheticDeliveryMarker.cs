// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MimeKit;

namespace MailFathom.SyntheticMail.Delivery;

/// <summary>The header a run stamps a submitted message with so it can recognize the delivered copy again.</summary>
/// <remarks>
/// <para>
/// A submission server may replace <c>Message-Id</c>, which is the whole reason an exchange reads the delivered copy
/// back rather than trusting the identifier it proposed — so the identifier cannot also be what finds that copy. This
/// header can be, because nothing between the two ends rewrites an unknown header field, and RFC 3501 requires every
/// IMAP server to answer <c>SEARCH HEADER</c> for any field name.
/// </para>
/// <para>
/// Its value is the proposed <c>Message-Id</c>. That is one value fewer to invent, and it leaves the delivered copy
/// carrying the corpus entry it came from, which is what makes a mailbox filled by this tool readable afterwards.
/// Only an exchange stamps one: a flat batch never looks for what it delivered, and a header added to every message
/// would change a corpus a seed is supposed to reproduce exactly.
/// </para>
/// </remarks>
internal static class SyntheticDeliveryMarker
{
    /// <summary>The header field name, in the <c>X-</c> space no standard header occupies.</summary>
    internal const string HeaderName = "X-MailFathom-Synthetic";

    /// <summary>Stamps a message so the delivered copy can be searched for.</summary>
    /// <param name="message">The composed message, which the caller still owns.</param>
    /// <param name="marker">The value to stamp, which is the message's proposed identifier.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static void Stamp(MimeMessage message, string marker)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(marker);

        message.Headers.Add(HeaderName, marker);
    }
}
