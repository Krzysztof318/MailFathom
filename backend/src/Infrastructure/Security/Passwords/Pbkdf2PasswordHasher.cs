// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.Access.Credentials;

namespace MailFathom.Infrastructure.Security.Passwords;

/// <summary>Stores a password as a PBKDF2-HMAC-SHA-512 derivation carrying its own salt and work parameters.</summary>
/// <remarks>
/// <para>
/// PBKDF2 rather than a memory-hard construction, and the reason is the platform rather than a judgement that it is
/// better. Argon2id and scrypt would each be a new package on the critical path of every authenticated request, with a
/// licence review, a supply-chain decision, and a native dependency behind it; PBKDF2-HMAC-SHA-512 ships in .NET, is
/// approved by every standards body this deployment might be read against, and at the iteration count below costs an
/// attacker with commodity hardware more per guess than the rate limiter in front of it will ever let them make.
/// Nothing here is load-bearing on that choice: the stored value names its construction, so adopting a memory-hard one
/// later is a second format version beside this and the rehash path below is already what carries existing rows over.
/// </para>
/// <para>
/// SHA-512 rather than SHA-256, because the GPU advantage an attacker gets over a defender is markedly smaller on
/// 64-bit arithmetic. The iteration count is the current OWASP figure for this pairing, and it is a constant rather than
/// a setting for the reason the password policy's own bounds are: a deployment that could lower it would be a
/// deployment whose weakest credential was decided by whoever edited a file.
/// </para>
/// <para>
/// Both operations are synchronous and read the password out of a span, so the plaintext is never copied into a string,
/// never crosses an await, and is gone the moment the caller clears the buffer it owns. The salt is fresh per call, so
/// two identical passwords store differently and a database dump answers nothing about which owners share one.
/// </para>
/// </remarks>
internal sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    /// <summary>The number of iterations a record written today carries.</summary>
    /// <remarks>OWASP's current figure for PBKDF2-HMAC-SHA-512. Raising it makes every existing record report itself as behind, which the verification below acts on while it still holds the plaintext.</remarks>
    internal const int CurrentIterations = 210_000;

    /// <summary>The number of octets of salt each record carries.</summary>
    /// <remarks>Sixteen, which is the width at which two records colliding by chance is not a thing that happens; a salt is not a secret and gains nothing from being longer.</remarks>
    internal const int SaltLength = 16;

    /// <summary>The number of octets each derivation produces.</summary>
    /// <remarks>Thirty-two, so the stored value carries a full 256 bits of the derived key. Deriving the algorithm's whole 64-octet output would double what a row holds without making a guess any more expensive, because the cost is the iteration count rather than the width.</remarks>
    internal const int DerivedKeyLength = 32;

    private static readonly HashAlgorithmName Digest = HashAlgorithmName.SHA512;

    /// <inheritdoc />
    public string Hash(ReadOnlySpan<char> password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var derivedKey = Derive(password, salt, CurrentIterations);

        return new PasswordHashRecord(CurrentIterations, salt, derivedKey).ToString();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The derived keys are compared with <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})" />,
    /// which is fixed-time over equal lengths — and they are equal by construction, because the presented password is
    /// derived to exactly the width the stored record carries. A record whose key is a different width therefore fails
    /// on the length rather than being compared, which says nothing an attacker did not already have: the width is a
    /// property of the release that wrote the row, not of the password.
    /// </remarks>
    public PasswordVerification Verify(string storedHash, ReadOnlySpan<char> password)
    {
        ArgumentNullException.ThrowIfNull(storedHash);

        if (!PasswordHashRecord.TryParse(storedHash, out var record))
        {
            return PasswordVerification.Failed;
        }

        var presentedKey = Derive(password, record.Salt, record.Iterations, record.DerivedKey.Length);

        try
        {
            if (!CryptographicOperations.FixedTimeEquals(presentedKey, record.DerivedKey))
            {
                return PasswordVerification.Failed;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(presentedKey);
        }

        return IsBehindCurrentPolicy(record)
            ? PasswordVerification.SucceededAndShouldBeRehashed
            : PasswordVerification.Succeeded;
    }

    /// <summary>Reports whether a record was written under weaker parameters than this release would write today.</summary>
    /// <remarks>
    /// Only a weaker record is replaced. One carrying more iterations than the current figure is left alone, because a
    /// deployment rolled back to an earlier release would otherwise quietly weaken every password its owners signed in
    /// with.
    /// </remarks>
    private static bool IsBehindCurrentPolicy(PasswordHashRecord record) =>
        record.Iterations < CurrentIterations
        || record.Salt.Length < SaltLength
        || record.DerivedKey.Length < DerivedKeyLength;

    private static byte[] Derive(
        ReadOnlySpan<char> password,
        ReadOnlySpan<byte> salt,
        int iterations,
        int derivedKeyLength = DerivedKeyLength)
    {
        var derivedKey = new byte[derivedKeyLength];

        Rfc2898DeriveBytes.Pbkdf2(password, salt, derivedKey, iterations, Digest);

        return derivedKey;
    }
}
