// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Application.Paging;

/// <summary>Reduces the filters a page was read under to the short stable text its cursor carries.</summary>
/// <remarks>
/// <para>
/// A keyset position names a page edge only within the filtered set it was computed for, so a cursor carries a digest
/// of that set and a query refuses one whose digest is not its own. Which fields go into the digest is each reading's
/// own question and stays in its own <c>ComputeFingerprint</c>; how they are reduced to text is this.
/// </para>
/// <para>
/// The digest is truncated because it distinguishes one caller's own filter sets rather than resisting a search for a
/// collision: a forged fingerprint buys a boundary inside a page that same caller is already entitled to read. That is
/// what makes this the weaker of the two schemes in the repository, and deliberately so.
/// <see cref="Emails.Mailboxes.EmailTimelineFilter" /> hashes a length-prefixed canonical text instead, so no value it
/// covers can be written to look like the field boundary — a stronger scheme for a surface where a filter is
/// caller-composed and long-lived. The two are not interchangeable: a cursor encodes the digest it was issued with, so
/// moving either reading onto the other's scheme invalidates every cursor a client is part-way through.
/// </para>
/// <para>
/// The separator is chosen because a filter value is not expected to contain it. That is an assumption rather than an
/// invariant — a free-text filter is refused only for being blank — and where it fails, two different filter sets
/// reduce to one digest and a cursor issued for the first is accepted for the second. The consequence stays inside the
/// bound above: both sets belong to the same caller, over the same account, and name a boundary that caller may
/// already read.
/// </para>
/// </remarks>
public static class PageFilterFingerprint
{
    /// <summary>Separates the fields, chosen because a filter value is not expected to contain it.</summary>
    private const char FieldSeparator = '\u001f';

    /// <summary>How many hexadecimal characters of the digest a cursor carries.</summary>
    private const int FingerprintLength = 16;

    /// <summary>Reduces one reading's filter values to the fingerprint its cursors carry.</summary>
    /// <param name="fields">The filter values, in a fixed order, with <see langword="null" /> for one nobody named.</param>
    /// <returns>The fingerprint.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fields" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Every field is written, absent ones included, so a filter a later build adds cannot produce the text an earlier
    /// build produced for a caller who named fewer.
    /// </remarks>
    public static string Of(params string?[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var material = string.Join(FieldSeparator, fields);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..FingerprintLength];
    }
}
