// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Whether the domain that authenticated is the domain the message displays as its sender.</summary>
/// <remarks>
/// <para>
/// This is MailFathom's own comparison of the authenticated domain against the <c>From</c> domain, and it is exact:
/// two domains align when they normalize to the same name. It is deliberately stricter than the relaxed alignment a
/// DMARC evaluator may apply, which is why <see cref="DmarcOutcome" /> is recorded separately rather than derived from
/// this — the receiving server's own DMARC verdict is the one that took the sender's published policy into account.
/// </para>
/// <para>
/// The <c>From</c> header takes no part in establishing the identity. It appears here only so that a message
/// authenticated as one domain while claiming to be from another is visible as exactly that.
/// </para>
/// </remarks>
public enum SenderDomainAlignment
{
    /// <summary>Nothing was compared, because no identity was established or the message displayed no usable sender.</summary>
    NotAssessed = 0,

    /// <summary>The authenticated domain and the <c>From</c> domain are the same name.</summary>
    Aligned = 1,

    /// <summary>The authenticated domain is not the domain the message displays as its sender.</summary>
    Misaligned = 2,
}
