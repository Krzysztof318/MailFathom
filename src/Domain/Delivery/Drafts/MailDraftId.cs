// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Drafts;

/// <summary>Identifies one draft this deployment holds, for as long as it is held.</summary>
/// <remarks>
/// A draft carries no idempotency identity, unlike <see cref="OutgoingEmailId" />'s record: asking twice for a draft is
/// two drafts rather than one delivery, because nothing about a draft can be duplicated onto somebody else's mail
/// server. So this is the whole of a draft's identity rather than a surrogate beside a composite, and it is what the
/// stored MIME, the appended copies, and every later revision are all keyed by.
/// </remarks>
public readonly record struct MailDraftId
{
    private MailDraftId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates a draft identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated draft identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static MailDraftId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A mail draft identifier cannot be empty.", nameof(value));
        }

        return new MailDraftId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
