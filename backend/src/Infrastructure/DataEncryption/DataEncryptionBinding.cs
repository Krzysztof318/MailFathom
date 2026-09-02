// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;

namespace MailFathom.Infrastructure.DataEncryption;

/// <summary>What a sealed value belongs to, authenticated into the value so that it cannot belong to anything else.</summary>
/// <remarks>
/// <para>
/// A binding is a purpose and a subject: what the value is, and whose it is. Both are authenticated but not encrypted,
/// which is what makes a sealed value refuse to open anywhere other than where it was written. A row copied between
/// accounts fails to open rather than opening as the wrong owner's credential, a value moved into a column that means
/// something else fails the same way, and so does a row restored from another deployment.
/// </para>
/// <para>
/// The identifier of the key that sealed the value joins the binding when the associated data is composed. Binding the
/// key identifier as well means a value cannot be presented as though another key had sealed it, so an attacker holding
/// one retired key cannot make a value appear current by rewriting the identifier beside it.
/// </para>
/// </remarks>
public readonly record struct DataEncryptionBinding
{
    /// <summary>Separates the parts of the associated data, chosen because no part may contain it.</summary>
    /// <remarks>
    /// Without a separator no part could contain, a subject ending where the next part begins would produce the same
    /// associated data as a different pair, and two values that must not open as one another would. The unit separator
    /// is a C0 control character that no identifier this system accepts can carry, and <see cref="Create" /> refuses one
    /// that does rather than escaping it.
    /// </remarks>
    private const char PartSeparator = '\u001F';

    private DataEncryptionBinding(DataEncryptionPurpose purpose, string subject)
    {
        this.Purpose = purpose;
        this.Subject = subject;
    }

    /// <summary>Gets what the sealed value is.</summary>
    public DataEncryptionPurpose Purpose { get; }

    /// <summary>Gets whose the sealed value is, in MailFathom's own naming rather than the remote system's.</summary>
    /// <remarks>An account identifier is a configured name the operator chose, so it carries no personal data into the associated data or into any diagnostic that reports a binding.</remarks>
    public string Subject { get; }

    /// <summary>Creates a binding.</summary>
    /// <param name="purpose">What the value is.</param>
    /// <param name="subject">Whose the value is.</param>
    /// <returns>The binding every seal and open of that value must use.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="subject" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="purpose" /> is the unspecified struct default, when <paramref name="subject" /> is
    /// empty, or when the subject carries the separator the associated data is composed with. Each is a defect in the
    /// caller rather than a configuration error: a value bound to nothing meaningful would still seal and would open
    /// under a binding nobody intended.
    /// </exception>
    public static DataEncryptionBinding Create(DataEncryptionPurpose purpose, string subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (!purpose.IsSpecified)
        {
            throw new ArgumentException(
                "The purpose is the default of the struct and names nothing a sealed value could be bound to.",
                nameof(purpose));
        }

        if (subject.Length == 0)
        {
            throw new ArgumentException("The subject of a sealed value cannot be empty.", nameof(subject));
        }

        if (subject.Contains(PartSeparator, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The subject carries the separator the associated data is composed with, which would let two different bindings compose the same associated data.",
                nameof(subject));
        }

        return new DataEncryptionBinding(purpose, subject);
    }

    /// <summary>Composes the associated data a value sealed under one key is authenticated against.</summary>
    /// <param name="keyId">The identifier of the key sealing or opening the value.</param>
    /// <returns>The authenticated bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keyId" /> is <see langword="null" />.</exception>
    /// <remarks>The composition is fixed rather than configurable, because a reader has to authenticate a value written by an older build.</remarks>
    internal byte[] ComposeAssociatedData(string keyId)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        return Encoding.UTF8.GetBytes($"{this.Purpose.Identity}{PartSeparator}{this.Subject}{PartSeparator}{keyId}");
    }

    /// <inheritdoc />
    public override string ToString() => $"{this.Purpose}/{this.Subject}";
}
