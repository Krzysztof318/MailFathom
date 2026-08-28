// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.Credentials;

/// <summary>Turns a password into the record a deployment stores, and judges a presented one against that record.</summary>
/// <remarks>
/// <para>
/// The port exists so that no use case, no endpoint, and no command ever holds an opinion about which construction is
/// in use, what its work parameters are, or how a salt is carried. All of that belongs to the one adapter behind this,
/// and moving to a different construction is a change to that adapter plus the rehash path <see cref="Verify" /> already
/// reports — never a change to anything that provisions or authenticates a credential.
/// </para>
/// <para>
/// Both members are synchronous and take a span, which is the shape the material has to be handled in. A password is
/// read for the bounded duration of one hash or one verification and is never copied or retained by an implementation;
/// a task-returning signature would put it behind an await, where the caller can no longer say when the buffer stops
/// being read. What the span points at is the caller's, and on both of MailFathom's paths it is a
/// <see langword="string" /> that nothing can wipe — so what a span buys here is a bounded reading rather than an
/// erasure, and no implementation may promise the second. Hashing is deliberately expensive — that is the point of an adaptive construction — so a caller on a
/// request path spends that cost knowingly rather than being offered a way to hide it.
/// </para>
/// <para>
/// The stored representation is opaque text to every caller. It carries its own algorithm identity, version, work
/// parameters, and random salt, so a record written by an earlier release is self-describing and no column beside it
/// has to be kept in step.
/// </para>
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>Produces a record no password matches, for comparing against a username this deployment holds nothing for.</summary>
    /// <returns>The stored representation of a password nothing retained.</returns>
    /// <remarks>
    /// The point is the cost rather than the value, which is why this belongs to the hasher rather than to its caller:
    /// what makes an unresolved username indistinguishable from a resolved one is that verifying against this costs
    /// what verifying against a real record costs, and only the adapter knows the weakest work parameters any record it
    /// would still accept may carry. A caller that derived its own decoy would pin it to today's parameters and reopen
    /// the difference the first time they are raised.
    /// </remarks>
    string HashDecoy();

    /// <summary>Produces the record to store for one password.</summary>
    /// <param name="password">The plaintext, which is read within the call and never retained.</param>
    /// <returns>The stored representation, carrying its algorithm, version, work parameters, and salt.</returns>
    /// <remarks>Two calls with one password return different records, because each carries its own random salt. Nothing may compare two records for equality.</remarks>
    string Hash(ReadOnlySpan<char> password);

    /// <summary>Judges a presented password against a stored record.</summary>
    /// <param name="storedHash">The record as it was stored, or any other text a row happened to hold.</param>
    /// <param name="password">The plaintext presented, which is read within the call and never retained.</param>
    /// <returns>What the comparison established, including whether the record is behind the current policy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="storedHash" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The comparison of the derived keys is constant-time. A record that will not parse answers
    /// <see cref="PasswordVerification.Failed" /> rather than raising, because the caller refuses an unreadable record
    /// and a wrong password identically and must not be given a fault to report differently.
    /// </remarks>
    PasswordVerification Verify(string storedHash, ReadOnlySpan<char> password);
}
