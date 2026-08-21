// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>One <c>Authentication-Results</c> header of a message, read but not yet believed.</summary>
/// <remarks>
/// <para>
/// The header is grouped rather than flattened because the grouping is the security property. RFC 8601 has every
/// producing server write its own identifier into the header it adds, and a consumer reads only the headers bearing the
/// identifier it trusts — so a set of outcomes with the identifier stripped off is a set nothing can be trusted from,
/// however true each outcome is.
/// </para>
/// <para>
/// Both bounds below exist because a message decides how many headers it carries and how much each of them says. What
/// falls past a bound is the tail of a repetition an attacker could have written, and the topmost headers — the ones a
/// receiving server adds and the only ones a trusted reading ever uses — are the ones kept.
/// </para>
/// </remarks>
/// <param name="AuthorityIdentifier">The authserv-id the producing server wrote, which is what a trusted reading matches on.</param>
/// <param name="Methods">The outcomes the header stated, in the order it wrote them.</param>
public sealed record AuthenticationResultsHeader(
    string AuthorityIdentifier,
    IReadOnlyList<ReportedAuthenticationMethod> Methods)
{
    /// <summary>The greatest number of <c>Authentication-Results</c> headers read from one message, counted from the top.</summary>
    public const int MaximumHeadersPerMessage = 16;

    /// <summary>The greatest number of method outcomes read from one header.</summary>
    public const int MaximumMethodsPerHeader = 32;

    /// <summary>The greatest number of properties read from one method outcome.</summary>
    public const int MaximumPropertiesPerMethod = 16;

    /// <summary>
    /// The greatest length one header's value may have before it is passed over unread. It is generous against every
    /// header a mail server actually writes and small enough that a message cannot spend its whole size allowance on a
    /// single header for a parser to walk.
    /// </summary>
    public const int MaximumHeaderValueLength = 4096;
}
