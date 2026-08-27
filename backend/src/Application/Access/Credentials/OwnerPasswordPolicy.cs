// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.Credentials;

/// <summary>What a password has to be before this deployment will store one.</summary>
/// <remarks>
/// <para>
/// Two bounds and one refusal, and deliberately nothing else. Composition rules — a digit, a capital, a symbol — make a
/// password harder to remember without making it harder to guess, which is why current guidance drops them in favour of
/// length; what is kept is the floor that makes a guessed password expensive and the ceiling that keeps an adaptive
/// hash from being handed a megabyte of work by whoever can reach the administrative surface.
/// </para>
/// <para>
/// Neither bound is configurable. A deployment that could lower the floor would be a deployment whose weakest
/// credential is decided by whoever edited a file, and the whole reason this method exists beside the stronger ones is
/// that it is the simple choice rather than the weak one.
/// </para>
/// <para>
/// The refusal of a control character is about what can be presented rather than about strength: RFC 7617 carries the
/// credential as one line of an HTTP header, and a carriage return or a line feed inside one is a value no client can
/// send whole. Refusing it at provisioning is what stops a password being stored that could never be used.
/// </para>
/// </remarks>
public static class OwnerPasswordPolicy
{
    /// <summary>The shortest password this deployment stores.</summary>
    /// <remarks>Twelve characters, which is the floor current guidance settles on for a secret a person chooses and a service stores under an adaptive hash.</remarks>
    public const int MinimumLength = 12;

    /// <summary>The longest password this deployment stores.</summary>
    /// <remarks>Long enough for any passphrase or generated value, and short enough that hashing one is bounded work: the construction below reads the whole password, so an unbounded length would be an unbounded cost per verification.</remarks>
    public const int MaximumLength = 256;

    /// <summary>Reports why a password cannot be stored, or that it can.</summary>
    /// <param name="password">The plaintext, which is read within the call and never retained.</param>
    /// <returns>The sentence an operator is answered with, or <see langword="null" /> when the password is acceptable.</returns>
    /// <remarks>
    /// The message says what the rule is and never what was written, so a refusal reaching a log, a terminal, or an
    /// HTTP response carries no part of the password — not its length, not the character that was refused, not a
    /// fragment of it.
    /// </remarks>
    public static string? FindRefusal(ReadOnlySpan<char> password)
    {
        if (password.Length < MinimumLength)
        {
            return $"A password is at least {MinimumLength} characters.";
        }

        if (password.Length > MaximumLength)
        {
            return $"A password is at most {MaximumLength} characters.";
        }

        foreach (var character in password)
        {
            if (char.IsControl(character))
            {
                return "A password carries no control characters, because the credential is presented as one line of an HTTP header.";
            }
        }

        return null;
    }
}
