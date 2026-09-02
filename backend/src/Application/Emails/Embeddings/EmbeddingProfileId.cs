// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings;

/// <summary>Identifies one registered embedding profile, which is one vector space this deployment has embedded into.</summary>
/// <remarks>
/// Separate from <see cref="EmbeddingProfileFingerprint" />, which identifies the geometry rather than the row: the
/// fingerprint is what activation resolves a declaration through, and this is what a stored vector's attribution points
/// at afterwards.
/// </remarks>
public readonly record struct EmbeddingProfileId
{
    private EmbeddingProfileId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates a profile identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated profile identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static EmbeddingProfileId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An embedding profile identifier cannot be empty.", nameof(value));
        }

        return new EmbeddingProfileId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
