// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.Signals;

/// <summary>One sender-authentication outcome a receiving server recorded for a message.</summary>
/// <param name="Method">The authentication method, as RFC 8601 names it: <c>spf</c>, <c>dkim</c>, <c>dmarc</c>, and the rest.</param>
/// <param name="Result">The outcome the method reached: <c>pass</c>, <c>fail</c>, <c>none</c>, and the rest of the RFC 8601 set.</param>
/// <param name="Detail">The properties the server wrote beside the outcome, or <see langword="null" /> when it wrote none.</param>
/// <param name="IsForwarded">Whether the outcome was preserved across a forwarding hop rather than reached by the receiving server itself.</param>
/// <remarks>
/// <para>
/// This is what the receiving server concluded at the moment it mattered, with the network context of the connection the
/// message arrived on. Nothing after delivery has that context, which is why these outcomes are recorded as facts of
/// their own rather than being re-derived.
/// </para>
/// <para>
/// A forwarded outcome comes out of the ARC chain of RFC 8617, which preserves SPF and DKIM across the hops where they
/// legitimately break. It is kept apart from a directly observed one because the two have different standing: the first
/// is a claim a relay signed, the second is what this mailbox's own server saw.
/// </para>
/// <para>
/// <paramref name="Detail" /> can name a sending domain, so it is personal data and is never logged.
/// </para>
/// </remarks>
public sealed record MessageAuthenticationResult(
    string Method,
    string Result,
    string? Detail,
    bool IsForwarded);
