// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Common.ClientAssertions;

/// <summary>Why key material could not be used for the half of the pair it was read as.</summary>
/// <remarks>
/// The vocabulary reaches an operator rather than a client: the deployment reports it at startup against the
/// configuration path the material was named at, and <c>mfctl</c> reports it against the file the operator passed. No
/// refusal of a request ever carries one, because by the time a request is judged the material has already proven
/// usable.
/// </remarks>
public enum ClientAssertionKeyFault
{
    /// <summary>The material is not PEM at all, so nothing about it names a key.</summary>
    NotPem = 0,

    /// <summary>The material is PEM carrying the other half of the pair: a private key where the public one belongs, or the reverse.</summary>
    /// <remarks>The one fault worth telling apart from every other, because it is the mistake the method exists to make impossible — a deployment configured with a private key would be holding exactly what key-pair authentication is for not holding.</remarks>
    WrongHalf = 1,

    /// <summary>The material is a password-protected private key, which nothing here unlocks.</summary>
    EncryptedPrivateKey = 2,

    /// <summary>The material is a key of a kind no permitted signature algorithm covers.</summary>
    UnsupportedAlgorithm = 3,

    /// <summary>The material is an RSA key shorter than the shortest modulus this deployment accepts a signature from.</summary>
    ModulusTooShort = 4,
}
