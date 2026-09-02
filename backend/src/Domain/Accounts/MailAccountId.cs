// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Accounts;

/// <summary>Identifies a configured mail account inside MailFathom.</summary>
public readonly record struct MailAccountId
{
    private MailAccountId(string value) => this.Value = value;

    /// <summary>Gets the stable account identifier.</summary>
    public string Value { get; }

    /// <summary>Creates an account identifier from configuration-owned text.</summary>
    /// <param name="value">The configured account identifier.</param>
    /// <returns>A validated account identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank.</exception>
    public static MailAccountId Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new MailAccountId(value.Trim());
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
