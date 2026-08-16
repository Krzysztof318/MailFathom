// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>What the receiving mail server established about the author a message displays in its <c>From</c> header.</summary>
/// <remarks>
/// <para>
/// This is a different question from <see cref="SenderAuthenticationOutcome" /> and the two routinely disagree. That one
/// answers whether <em>an</em> identity authenticated, which for a relay, a mailing list, or a delivery provider is the
/// identity of whoever handed the message over. This one answers whether the identity a mail client shows the reader is
/// the one that authenticated, which is the question every impersonation is an attempt to have answered wrongly.
/// </para>
/// <para>
/// The <c>From</c> header takes no part in reaching it. It is attacker-controlled message content, so it says who the
/// message <em>claims</em> to be from and never who it is from; what establishes the author is the trusted receiving
/// server's own DMARC result, or an authenticated identity whose domain is exactly the displayed one.
/// </para>
/// </remarks>
public enum AuthorAuthenticationOutcome
{
    /// <summary>The trusted evidence is not enough to conclude either way about the displayed author.</summary>
    /// <remarks>
    /// It is deliberately not <see cref="Failed" />. A message signed by a subdomain of the displayed domain, with no
    /// usable DMARC result to say whether the sender's own policy permits that, is the ordinary case here: MailFathom
    /// evaluates no policy, so it does not know, and a verdict that said the author failed would be inventing a refusal
    /// the receiving server never made.
    /// </remarks>
    NotEstablished = 0,

    /// <summary>The trusted receiving server evaluated the displayed author and reported that it did not hold.</summary>
    /// <remarks>
    /// Reached from <see cref="DmarcOutcome.Fail" /> and nothing else, because DMARC is the one result that is about the
    /// displayed domain rather than about a transport identity. A DKIM signature that did not verify says nothing about
    /// the author, since the signature may never have belonged to them.
    /// </remarks>
    Failed = 1,

    /// <summary>The trusted receiving server established that the displayed author authenticated.</summary>
    Authenticated = 2,
}
