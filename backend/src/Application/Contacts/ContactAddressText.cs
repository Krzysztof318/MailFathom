// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Contacts;

/// <summary>The one thing every reader of a caller's address text has to refuse before the domain sees it.</summary>
/// <remarks>
/// <para>
/// An address reaches the book as text somebody typed rather than as an addr-spec a mail parser produced, and the header
/// form is what they have in front of them. <c>Anna Kowalska &lt;anna@example.test&gt;</c> is already refused, because
/// the display name carries whitespace the domain will not admit in a local part — but the brackets alone are not:
/// <c>&lt;anna@example.test&gt;</c> splits on the last at-sign into two halves that each look usable, so it is stored as
/// an address nobody's mail will ever arrive under and a lookup for the person it names answers nobody.
/// </para>
/// <para>
/// It is stated once here rather than at each reader, because the three that exist refuse it with different failures — a
/// malformed identifier at the protocol boundary, an invalid record inside a write, a refusal sentence on the
/// administrative endpoint — and a rule spelled three times is a rule one of them will eventually stop applying. The
/// domain type is deliberately left alone: it is handed addr-specs a mail parser has already unwrapped, and a header
/// form never reaches it from that direction.
/// </para>
/// </remarks>
public static class ContactAddressText
{
    /// <summary>Reports whether the text is an address written in the angle brackets a header wraps one in.</summary>
    /// <param name="trimmedAddress">The caller's text, already trimmed.</param>
    /// <returns><see langword="true" /> when the text carries an angle bracket anywhere in it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="trimmedAddress" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Either bracket alone is enough to refuse: an address carries neither character, so text holding one was copied
    /// out of a header rather than written as an address.
    /// </remarks>
    public static bool IsAngleAddress(string trimmedAddress)
    {
        ArgumentNullException.ThrowIfNull(trimmedAddress);

        return trimmedAddress.Contains('<', StringComparison.Ordinal)
            || trimmedAddress.Contains('>', StringComparison.Ordinal);
    }
}
