// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Accounts;

/// <summary>Holds the text a request uses to name an account, before anything has decided which account that is.</summary>
/// <remarks>
/// <para>
/// An account can be named two ways — by the <see cref="MailAccountId" /> an operator configured or by the
/// <see cref="MailAccountDisplayName" /> it is published under — and a caller is not required to know which of the two
/// it is holding. The distinction is settled where the accounts the caller's owner owns are known, so what travels from
/// a protocol boundary into a use case is this: text that has been proven safe to carry and nothing more.
/// </para>
/// <para>
/// Both spellings are the owner's own words and are unique within that owner rather than across the deployment, so the
/// same text can name a different mailbox for somebody else. That is why it is resolved against one owner's accounts
/// and never against the deployment's: text naming nothing, text naming another owner's account, and text naming an
/// account this deployment stopped serving are one refusal, and a caller cannot learn from it which of the three they
/// were holding.
/// </para>
/// <para>
/// It is deliberately not a <see cref="MailAccountId" />. Reading caller text as an identifier before it has been
/// matched would put a value nobody configured into a type whose whole meaning is that MailFathom issued it, and the
/// refusal for text that names no account would then be indistinguishable from a lookup of a real identity.
/// </para>
/// </remarks>
public readonly record struct MailAccountSelector
{
    /// <summary>The greatest length text naming an account may carry.</summary>
    /// <remarks>
    /// The bound is on untrusted input rather than on configuration: the text is echoed in the refusal a caller reads
    /// when it names no account, so an unbounded value would be a way to write a paragraph into that contract.
    /// </remarks>
    public const int MaximumLength = 256;

    private MailAccountSelector(string value) => this.Value = value;

    /// <summary>Gets the text the request named an account with, trimmed.</summary>
    public string Value { get; }

    /// <summary>Creates a selector from the text a request carried.</summary>
    /// <param name="value">The text naming an account.</param>
    /// <returns>A selector holding the trimmed text.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank, longer than <see cref="MaximumLength" />, or contains a control character.</exception>
    public static MailAccountSelector Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmed = value.Trim();

        if (trimmed.Length > MaximumLength)
        {
            throw new ArgumentException($"Text naming an account cannot be longer than {MaximumLength} characters.", nameof(value));
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("Text naming an account cannot contain control characters.", nameof(value));
        }

        return new MailAccountSelector(trimmed);
    }

    /// <summary>Creates the selector that names one account by its identifier.</summary>
    /// <param name="accountId">The account identifier to name.</param>
    /// <returns>A selector holding the identifier's text.</returns>
    /// <remarks>
    /// This is how code that already holds an identity reaches a contract expressed in selectors, so that path spells
    /// the conversion out instead of round-tripping through raw text.
    /// </remarks>
    public static MailAccountSelector For(MailAccountId accountId) => new(accountId.Value);

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
