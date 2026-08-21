// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>Carries the draft a composition produced, or the reason it produced none.</summary>
/// <remarks>
/// A refusal is a result rather than an exception for the reason <see cref="AuthoredEmailComposition" />'s is: the
/// caller acts on it directly, nothing is written down, and the draft simply does not exist. Everything a draft is
/// refused for is something a send is refused for as well — an injected header, a body nobody wrote, a bound — so the
/// refusal is the same type and reads identically whichever the author was asking for.
/// </remarks>
public sealed record MailDraftComposition
{
    private MailDraftComposition(ComposedMailDraft? draft, AuthoredEmailRefusal? refusal)
    {
        this.Draft = draft;
        this.Refusal = refusal;
    }

    /// <summary>Gets the composed draft, or <see langword="null" /> when the composition was refused.</summary>
    public ComposedMailDraft? Draft { get; }

    /// <summary>Gets why no draft was composed, or <see langword="null" /> when one was.</summary>
    public AuthoredEmailRefusal? Refusal { get; }

    /// <summary>Gets whether a draft was composed.</summary>
    public bool IsComposed => this.Draft is not null;

    /// <summary>Reports a draft built from everything the author wrote.</summary>
    /// <param name="draft">The composed draft.</param>
    /// <returns>A composed result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="draft" /> is <see langword="null" />.</exception>
    public static MailDraftComposition Composed(ComposedMailDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return new MailDraftComposition(draft, refusal: null);
    }

    /// <summary>Reports that nothing was composed, and why.</summary>
    /// <param name="refusal">What was wrong and where.</param>
    /// <returns>A refused result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    public static MailDraftComposition Refused(AuthoredEmailRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        return new MailDraftComposition(draft: null, refusal);
    }
}
