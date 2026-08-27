// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MailFathom.Infrastructure.Security.Passwords;

/// <summary>The self-describing text one password is stored as, and the reading that recovers it.</summary>
/// <remarks>
/// <para>
/// A stored password is <c>$mf1$pbkdf2-sha512$i=&lt;iterations&gt;$&lt;salt&gt;$&lt;derived key&gt;</c>, with both
/// binary fields in standard Base64. Every parameter a verification needs travels inside the value, which is the whole
/// point: the column beside it holds nothing that has to be kept in step, a record written by an earlier release
/// verifies under the parameters it was written with, and raising the work parameters is a change to one constant
/// rather than a migration over every row.
/// </para>
/// <para>
/// The leading <c>mf1</c> is this repository's own format version rather than the algorithm's. It is what a later
/// release reads first to decide whether it understands the rest at all, so moving to a different construction —
/// Argon2id, say, once a reviewed dependency carries it — is a second version this reading refuses rather than a
/// different field order it might misread as the first.
/// </para>
/// <para>
/// The shape is deliberately close to the modular crypt format an operator may recognize from <c>/etc/shadow</c> and
/// from other services, so a value seen in a database dump reads as a password hash rather than as something to
/// investigate. It is not that format and is never handed to one: nothing outside this assembly parses it.
/// </para>
/// </remarks>
internal sealed record PasswordHashRecord(int Iterations, byte[] Salt, byte[] DerivedKey)
{
    /// <summary>The format version this release writes, which is the first field of every value it produces.</summary>
    internal const string FormatVersion = "mf1";

    /// <summary>The construction this format version carries, named the way its parameters are read.</summary>
    internal const string AlgorithmName = "pbkdf2-sha512";

    private const char FieldSeparator = '$';
    private const string IterationsPrefix = "i=";

    /// <summary>Writes the record as the text a row stores.</summary>
    /// <returns>The stored representation.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{FieldSeparator}{FormatVersion}{FieldSeparator}{AlgorithmName}{FieldSeparator}{IterationsPrefix}{this.Iterations}{FieldSeparator}{Convert.ToBase64String(this.Salt)}{FieldSeparator}{Convert.ToBase64String(this.DerivedKey)}");

    /// <summary>Reads a stored value back into its parameters.</summary>
    /// <param name="storedHash">The text a row held, which may be anything at all.</param>
    /// <param name="record">The parameters when the value is one this release understands; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the value was read.</returns>
    /// <remarks>
    /// Every failure answers <see langword="false" /> rather than raising, including a value written under a format
    /// version this release does not implement. The caller refuses an unreadable record exactly as it refuses a wrong
    /// password, so a fault here would be a way to tell the two apart.
    /// </remarks>
    internal static bool TryParse(string storedHash, [NotNullWhen(true)] out PasswordHashRecord? record)
    {
        record = null;

        if (storedHash is null)
        {
            return false;
        }

        // A leading separator means the first field is empty, which is what makes the version the second element and
        // keeps the value visually a modular-crypt-shaped string rather than one that starts with its own version.
        var fields = storedHash.Split(FieldSeparator);

        if (fields is not ["", FormatVersion, AlgorithmName, var iterationsField, var saltField, var derivedKeyField])
        {
            return false;
        }

        if (!iterationsField.StartsWith(IterationsPrefix, StringComparison.Ordinal)
            || !int.TryParse(
                iterationsField.AsSpan(IterationsPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var iterations)
            || iterations < 1)
        {
            return false;
        }

        if (!TryDecode(saltField, out var salt) || !TryDecode(derivedKeyField, out var derivedKey))
        {
            return false;
        }

        record = new PasswordHashRecord(iterations, salt, derivedKey);

        return true;
    }

    /// <summary>Decodes one Base64 field into the octets it carries.</summary>
    /// <remarks>The buffer is three octets per four characters rounded up, which is the most any field of that length can decode to; the decoder reports what it actually wrote and the surplus is trimmed rather than kept.</remarks>
    private static bool TryDecode(string field, [NotNullWhen(true)] out byte[]? decoded)
    {
        decoded = null;

        var buffer = new byte[((field.Length / 4) + 1) * 3];

        if (!Convert.TryFromBase64String(field, buffer, out var written) || written == 0)
        {
            return false;
        }

        decoded = buffer[..written];

        return true;
    }
}
