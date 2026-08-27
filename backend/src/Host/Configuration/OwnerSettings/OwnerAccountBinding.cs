// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.OwnerSettings;

/// <summary>The owner's record as it bound, or the sentences saying why the document is not one.</summary>
/// <remarks>
/// A refusal is a result rather than an exception because both callers act on it directly: a document read back is
/// refused to whoever asked for the owner's view, and a candidate document is refused to the administrator who wrote
/// it, in the same shape an unknown property or a stale version arrives in.
/// </remarks>
internal sealed record OwnerAccountBinding
{
    private OwnerAccountBinding(OwnerAccountOptions? owner, IReadOnlyList<string> refusals)
    {
        this.Owner = owner;
        this.Refusals = refusals;
    }

    /// <summary>Gets the bound record, or <see langword="null" /> when the document was refused.</summary>
    public OwnerAccountOptions? Owner { get; }

    /// <summary>Gets one sentence per refusal, empty when the document bound.</summary>
    public IReadOnlyList<string> Refusals { get; }

    /// <summary>Gets whether the document is this owner's record.</summary>
    public bool IsBound => this.Owner is not null;

    /// <summary>Reports a document that bound and passed every rule an account is declared under.</summary>
    /// <param name="owner">The bound record.</param>
    /// <returns>A bound result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="owner" /> is <see langword="null" />.</exception>
    public static OwnerAccountBinding Bound(OwnerAccountOptions owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return new OwnerAccountBinding(owner, []);
    }

    /// <summary>Reports a document that is not a record this deployment would keep.</summary>
    /// <param name="refusals">The sentences naming what must change, of which there is at least one.</param>
    /// <returns>A refused result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusals" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="refusals" /> is empty, which would be a refusal saying nothing.</exception>
    public static OwnerAccountBinding Refused(IReadOnlyList<string> refusals)
    {
        ArgumentNullException.ThrowIfNull(refusals);

        if (refusals.Count == 0)
        {
            throw new ArgumentException("A refused document names at least one thing to correct.", nameof(refusals));
        }

        return new OwnerAccountBinding(owner: null, refusals);
    }
}
