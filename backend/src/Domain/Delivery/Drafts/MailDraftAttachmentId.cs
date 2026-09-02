// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Drafts;

/// <summary>Identifies one file staged against a draft, for as long as that draft holds it.</summary>
/// <remarks>
/// It is a surrogate rather than the file's name, because a name is the author's and two files an author attached may
/// share one. What removes a staged file therefore names this, so an author who attached the same name twice removes
/// the one they meant rather than whichever the ordering happened to put first.
/// </remarks>
public readonly record struct MailDraftAttachmentId
{
    private MailDraftAttachmentId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates a staged-file identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static MailDraftAttachmentId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A mail draft attachment identifier cannot be empty.", nameof(value));
        }

        return new MailDraftAttachmentId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
