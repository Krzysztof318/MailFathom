// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>Identifies the owner whose mail a unit of work is acting on.</summary>
/// <remarks>
/// <para>
/// An owner is who mail belongs to: a mail account has exactly one, and the mail beneath that account is that owner's.
/// The identity is generated rather than chosen, so it names nobody outside this deployment and carries nothing about
/// the person — a display name, an address, and every other personal detail belong to the owner's own record rather
/// than to the value every account row points at.
/// </para>
/// <para>
/// It lives beside the permission vocabulary because it is the second axis an access decision is taken on, and the two
/// are asked together: a permission says what a caller may do and this says whose mail they may do it to. It is
/// deliberately not part of the permission set — no permission names an owner, and holding one says nothing about who
/// is being acted for.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and is not an owner. It reports itself through
/// <see cref="IsSpecified" /> so a value that names nobody cannot be mistaken for one that does, and
/// <see cref="Create" /> refuses the empty identifier outright.
/// </para>
/// </remarks>
public readonly record struct MailOwnerId
{
    private MailOwnerId(Guid value) => this.Value = value;

    /// <summary>Gets the generated owner identity, or the empty identifier when this value names nobody.</summary>
    public Guid Value { get; }

    /// <summary>Gets whether this value names an owner at all.</summary>
    public bool IsSpecified => this.Value != Guid.Empty;

    /// <summary>Creates an owner identity from a persisted identifier.</summary>
    /// <param name="value">The generated identifier the owner's record is keyed by.</param>
    /// <returns>The owner identity.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is the empty identifier, which names nobody.</exception>
    public static MailOwnerId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An owner identity is a generated identifier and is never the empty one.", nameof(value));
        }

        return new MailOwnerId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
