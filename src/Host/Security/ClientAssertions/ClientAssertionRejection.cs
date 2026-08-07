// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Security.ClientAssertions;

/// <summary>Why a presented client assertion was refused.</summary>
/// <remarks>
/// Every value produces the same response, so this vocabulary exists for the server log and never for the caller. A
/// client that learned the difference between an assertion nobody's key signed and one whose identifier had already been
/// spent would learn which of the two it is worth trying again, which is one bit more than a refusal is allowed to say.
/// </remarks>
internal enum ClientAssertionRejection
{
    /// <summary>The request carried no <c>Authorization</c> header at all.</summary>
    CredentialMissing = 0,

    /// <summary>The header was present but is not a <c>Bearer</c> credential this endpoint could read.</summary>
    CredentialMalformed = 1,

    /// <summary>The credential is not a JSON Web Token declaring itself a MailFathom client assertion.</summary>
    NotAnAssertion = 2,

    /// <summary>No configured public key verifies the signature, or the key that does has passed its own lifetime.</summary>
    /// <remarks>The two are one value deliberately. A key whose lifetime has ended is a key this deployment no longer accepts, and telling that apart from one it never held would answer whether a name exists.</remarks>
    SignatureUnrecognized = 3,

    /// <summary>The signature verifies, but the assertion claims something the endpoint does not accept.</summary>
    /// <remarks>A missing or wrong audience, an absent expiry, one already passed, one further ahead than the permitted window, or a missing or over-long replay identifier.</remarks>
    ClaimsUnacceptable = 4,

    /// <summary>The signature verifies and the claims are acceptable, but this process has already served an assertion carrying that identifier.</summary>
    IdentifierAlreadySpent = 5,
}
