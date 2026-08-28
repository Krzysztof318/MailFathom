// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Security.Passwords;

/// <summary>Why a presented username-and-password credential was refused.</summary>
/// <remarks>
/// Every value produces the same response, so this vocabulary exists for the server log and never for the caller. A
/// client that learned the difference between an unknown username and a wrong password would learn that an account
/// exists, which is one bit more than a refusal is allowed to say — and a client that learned it had been rate-limited
/// would learn how to pace its guessing.
/// </remarks>
public enum OwnerPasswordRejection
{
    /// <summary>The request carried no <c>Authorization</c> header at all.</summary>
    CredentialMissing = 0,

    /// <summary>The header was present but is not a <c>Basic</c> credential this endpoint could read.</summary>
    CredentialMalformed = 1,

    /// <summary>A readable credential was presented whose user-id is not a username this deployment could have issued.</summary>
    UsernameUnusable = 2,

    /// <summary>A readable credential was presented that resolves nothing, matches no password, or names a credential that has been turned off.</summary>
    /// <remarks>The four cases the acceptance criteria name collapse into this one deliberately: they are refused along one path, at the same cost, and telling them apart is exactly what an attacker enumerating accounts is trying to do.</remarks>
    CredentialUnrecognized = 3,

    /// <summary>The source or the username has spent its attempts for the current period, so no password was checked.</summary>
    TooManyAttempts = 4,
}
