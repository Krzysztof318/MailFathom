// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Security;

/// <summary>Why a presented MCP credential was refused.</summary>
/// <remarks>
/// Every value produces the same response, so this vocabulary exists for the server log and never for the caller. A
/// client that learned the difference between an unrecognized key and an expired one would learn that a key it holds
/// once existed, which is one bit more than a refusal is allowed to say.
/// </remarks>
public enum ApiKeyRejection
{
    /// <summary>The request carried no <c>Authorization</c> header at all.</summary>
    CredentialMissing = 0,

    /// <summary>The header was present but is not a <c>Bearer</c> credential this endpoint could read.</summary>
    CredentialMalformed = 1,

    /// <summary>A well-formed credential was presented that matches no configured key.</summary>
    CredentialUnrecognized = 2,

    /// <summary>A well-formed credential was presented that matches a configured key whose lifetime has ended.</summary>
    CredentialExpired = 3,
}
