// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Which check the receiving server established an identity with.</summary>
/// <remarks>
/// Only the two methods that carry an identity appear here. DMARC is not one of them: it reports whether an
/// already-authenticated domain lines up with the displayed sender, so it qualifies a verdict rather than producing one,
/// and it is recorded as <see cref="DmarcOutcome" /> instead.
/// </remarks>
public enum SenderAuthenticationMethod
{
    /// <summary>No identity was established, so nothing named one.</summary>
    None = 0,

    /// <summary>A DKIM signature verified against a key the signing domain publishes.</summary>
    /// <remarks>
    /// The stronger of the two claims and therefore the authoritative one wherever both are present. It is cryptographic
    /// rather than topological, and it survives the forwarding hops that legitimately break an SPF check.
    /// </remarks>
    DomainKeysIdentifiedMail = 1,

    /// <summary>The envelope sender passed the SPF policy the connecting address was checked against.</summary>
    SenderPolicyFramework = 2,
}
