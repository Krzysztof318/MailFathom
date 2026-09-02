// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;

namespace MailFathom.Common.OAuth;

/// <summary>The opaque value an authorization request carries and the redirect back to this process must echo.</summary>
/// <remarks>
/// It is what binds a returned authorization code to the request that asked for one, so a redirect arriving with
/// anything else was produced by some other exchange and the code it carries is not this process's to redeem. The
/// value is therefore generated the way a credential is — from a cryptographically secure source, per request — and
/// compared ordinally rather than parsed.
/// </remarks>
public static class AntiForgeryState
{
    /// <summary>The entropy behind one value, which renders as 32 hexadecimal characters.</summary>
    private const int EntropyByteCount = 16;

    /// <summary>Creates the opaque value the redirect must echo, which is what binds a returned code to this request.</summary>
    /// <returns>Hexadecimal text over 128 bits of cryptographically secure randomness.</returns>
    public static string Create() => Convert.ToHexString(RandomNumberGenerator.GetBytes(EntropyByteCount));
}
