// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Security.Passwords;

/// <summary>The two halves of one presented Basic credential, with the password still erasable.</summary>
/// <remarks>
/// <para>
/// It exists so a password read off a request has an owner and a lifetime. The characters stay in the pinned buffer
/// they were decoded into and are reached only as a span, so nothing between here and the key derivation copies them
/// into a string the collector would keep until it felt like moving it. Disposing clears the whole buffer, and every
/// caller disposes within the request that produced it.
/// </para>
/// <para>
/// The user-id is an ordinary string, because it is not a secret and is about to become a canonical username that gets
/// indexed, logged where a credential identifier is not yet known, and written into a refusal an operator reads. What
/// is never written anywhere is <see cref="Password" />, which is why <see cref="ToString" /> reports neither half:
/// the user-id would be safe on its own, and a value that printed one half would be a value somebody eventually
/// printed while assuming it printed neither.
/// </para>
/// </remarks>
public sealed class PresentedBasicCredential : IDisposable
{
    private readonly char[] decodedCredential;
    private readonly int passwordOffset;
    private readonly int passwordLength;

    /// <summary>Initializes a credential over the buffer its two halves were decoded into.</summary>
    /// <param name="userId">The name presented, exactly as it was written.</param>
    /// <param name="decodedCredential">The buffer holding the decoded credential, which this instance takes ownership of.</param>
    /// <param name="passwordOffset">Where the password starts in that buffer.</param>
    /// <param name="passwordLength">How many characters of it the password is.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="userId" /> or <paramref name="decodedCredential" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the password does not lie inside the buffer.</exception>
    public PresentedBasicCredential(
        string userId,
        char[] decodedCredential,
        int passwordOffset,
        int passwordLength)
    {
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(decodedCredential);
        ArgumentOutOfRangeException.ThrowIfNegative(passwordOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(passwordLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(passwordOffset + passwordLength, decodedCredential.Length);

        this.UserId = userId;
        this.decodedCredential = decodedCredential;
        this.passwordOffset = passwordOffset;
        this.passwordLength = passwordLength;
    }

    /// <summary>Gets the name presented, as written and before it is folded into its canonical form.</summary>
    public string UserId { get; }

    /// <summary>Gets the password presented, for the duration of this instance.</summary>
    /// <remarks>Reading it after <see cref="Dispose" /> yields a cleared span rather than the password, which is a wrong answer rather than a leaked one; nothing in this process reads it twice.</remarks>
    public ReadOnlySpan<char> Password => this.decodedCredential.AsSpan(this.passwordOffset, this.passwordLength);

    /// <inheritdoc />
    /// <remarks>Clears the whole buffer rather than the password's own range, because the user-id sitting in front of it is a copy nothing needs once the string above exists.</remarks>
    public void Dispose() => this.decodedCredential.AsSpan().Clear();

    /// <inheritdoc />
    /// <remarks>Neither half, so no diagnostic, log template, or exception message can print a presented credential by rendering the object it arrived in.</remarks>
    public override string ToString() => nameof(PresentedBasicCredential);
}
