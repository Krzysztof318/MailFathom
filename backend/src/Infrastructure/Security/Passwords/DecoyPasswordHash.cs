// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.Access.Credentials;

namespace MailFathom.Infrastructure.Security.Passwords;

/// <summary>The stored record a username this deployment holds nothing for is compared against.</summary>
/// <remarks>
/// <para>
/// A username that resolves nothing would otherwise be refused in the time of one indexed read, while a username that
/// resolves something costs a deliberately expensive derivation on top of it — and a client timing the difference
/// would be enumerating the accounts this deployment holds. Verifying against this record instead makes both answers
/// cost the same.
/// </para>
/// <para>
/// It is a type of its own so that it can be derived once for the process rather than once per request. The
/// authenticator that uses it reads the credential store through the request's own context and is therefore scoped;
/// deriving the decoy in that constructor would spend a full derivation on every request before anything had been
/// checked, which is the cost the rate limiter exists to bound and would hand an unauthenticated caller a way around
/// it.
/// </para>
/// <para>
/// The password behind it is random and is cleared before this object exists, so nothing can present the credential it
/// would accept.
/// </para>
/// </remarks>
public sealed class DecoyPasswordHash
{
    private const string PasswordAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Derives the record, once.</summary>
    /// <param name="passwordHasher">What the decoy password is derived with, so the decoy costs what a real record costs.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="passwordHasher" /> is <see langword="null" />.</exception>
    public DecoyPasswordHash(IPasswordHasher passwordHasher)
    {
        ArgumentNullException.ThrowIfNull(passwordHasher);

        var decoyPassword = GC.AllocateArray<char>(OwnerPasswordPolicy.MinimumLength, pinned: true);

        try
        {
            RandomNumberGenerator.GetItems(PasswordAlphabet, decoyPassword);

            this.Value = passwordHasher.Hash(decoyPassword);
        }
        finally
        {
            decoyPassword.AsSpan().Clear();
        }
    }

    /// <summary>The stored representation, in the same form a provisioned credential holds.</summary>
    public string Value { get; }
}
