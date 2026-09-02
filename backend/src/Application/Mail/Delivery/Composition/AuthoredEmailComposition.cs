// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>Carries the message a composition produced, or the reason it produced none.</summary>
/// <remarks>
/// A refusal is a result rather than an exception because the caller acts on it directly: an author is told which field
/// to correct and nothing is written down, so the send simply does not exist. Raising instead would make every
/// boundary that authors a message translate an exception back into the same three facts this already carries.
/// </remarks>
public sealed record AuthoredEmailComposition
{
    private AuthoredEmailComposition(ComposedOutgoingEmail? email, AuthoredEmailRefusal? refusal)
    {
        this.Email = email;
        this.Refusal = refusal;
    }

    /// <summary>Gets the composed message, or <see langword="null" /> when the composition was refused.</summary>
    public ComposedOutgoingEmail? Email { get; }

    /// <summary>Gets why no message was composed, or <see langword="null" /> when one was.</summary>
    public AuthoredEmailRefusal? Refusal { get; }

    /// <summary>Gets whether a message was composed.</summary>
    public bool IsComposed => this.Email is not null;

    /// <summary>Reports a message built from everything the author wrote.</summary>
    /// <param name="email">The composed message.</param>
    /// <returns>A composed result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="email" /> is <see langword="null" />.</exception>
    public static AuthoredEmailComposition Composed(ComposedOutgoingEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);

        return new AuthoredEmailComposition(email, refusal: null);
    }

    /// <summary>Reports that nothing was composed, and why.</summary>
    /// <param name="refusal">What was wrong and where.</param>
    /// <returns>A refused result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    public static AuthoredEmailComposition Refused(AuthoredEmailRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        return new AuthoredEmailComposition(email: null, refusal);
    }

    /// <summary>Reports that nothing was composed, from the parts a refusal is made of.</summary>
    /// <param name="reason">What was wrong.</param>
    /// <param name="field">The part of the authored message it was wrong in.</param>
    /// <param name="bound">The number that was exceeded, when the reason is a bound.</param>
    /// <returns>A refused result.</returns>
    public static AuthoredEmailComposition Refused(
        AuthoredEmailRefusalReason reason,
        AuthoredEmailField field,
        long? bound = null) =>
        Refused(new AuthoredEmailRefusal(reason, field, bound));
}
