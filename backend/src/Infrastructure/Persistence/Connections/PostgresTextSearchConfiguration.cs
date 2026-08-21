// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Connections;

/// <summary>Names the PostgreSQL text search configuration the lexical index is built with.</summary>
/// <remarks>
/// <para>
/// The configuration is stated rather than left to the server's <c>default_text_search_config</c> for two reasons. It
/// decides how every indexed word is stemmed and which words are dropped as stop words, so changing it changes what the
/// index contains rather than only how a query is written; a value inherited from a session setting could differ
/// between the process that wrote a row and the one that queries it, and the mismatch would show up as missing search
/// results rather than as an error. The single-argument <c>to_tsvector</c> is also not immutable for exactly that
/// reason, so PostgreSQL refuses it in the generated column this value exists to build.
/// </para>
/// <para>
/// The name is interpolated into that column's definition, which is why it is checked against a closed set instead of
/// being pattern-matched. A configuration outside the set is refused rather than passed through: an unknown name would
/// either fail schema creation at a point far from the configuration mistake, or — for a name that does exist but was
/// not meant — silently index the whole mailbox under the wrong language.
/// </para>
/// </remarks>
public sealed record PostgresTextSearchConfiguration
{
    /// <summary>The configurations a stock PostgreSQL server ships, which is the set MailFathom accepts.</summary>
    private static readonly string[] SupportedConfigurationNames =
    [
        "simple", "arabic", "armenian", "basque", "catalan", "danish", "dutch", "english", "finnish", "french",
        "german", "greek", "hindi", "hungarian", "indonesian", "irish", "italian", "lithuanian", "nepali",
        "norwegian", "portuguese", "romanian", "russian", "serbian", "spanish", "swedish", "tamil", "turkish",
        "yiddish",
    ];

    private PostgresTextSearchConfiguration(string value) => this.Value = value;

    /// <summary>Gets the configuration used when a deployment names none.</summary>
    /// <remarks>
    /// <c>simple</c> neither stems nor drops stop words, which is the honest default for a mailbox: the language of a
    /// message is not known when it is indexed, and a language-specific configuration applied to mail written in
    /// another one stems words into forms no query produces. A deployment whose mail is reliably in one language sets
    /// that language and gains its stemming.
    /// </remarks>
    public static PostgresTextSearchConfiguration Default { get; } = new("simple");

    /// <summary>Gets the names a deployment may configure, in the order they are reported to an operator.</summary>
    public static IReadOnlyList<string> SupportedNames => SupportedConfigurationNames;

    /// <summary>Gets the configuration name as PostgreSQL knows it.</summary>
    public string Value { get; }

    /// <summary>Reports whether a configured name is one MailFathom accepts.</summary>
    /// <param name="candidate">The name a deployment configured, which may be blank or unset.</param>
    /// <returns><see langword="true" /> when the name is supported; otherwise <see langword="false" />.</returns>
    public static bool IsSupported(string? candidate) =>
        candidate is not null && SupportedConfigurationNames.Contains(candidate, StringComparer.Ordinal);

    /// <summary>Creates a configuration from a supported name.</summary>
    /// <param name="name">The configuration name.</param>
    /// <returns>The validated configuration.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name" /> is not a supported configuration.</exception>
    /// <remarks>
    /// Comparison is ordinal and case-sensitive because PostgreSQL folds an unquoted identifier to lower case and these
    /// names are all lower case: accepting <c>English</c> here would mean writing a name into the schema that differs
    /// from the one an operator can look up in <c>pg_ts_config</c>.
    /// </remarks>
    public static PostgresTextSearchConfiguration Create(string name)
    {
        if (!IsSupported(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a supported PostgreSQL text search configuration. Supported configurations are: {string.Join(", ", SupportedConfigurationNames)}.",
                nameof(name));
        }

        return new PostgresTextSearchConfiguration(name);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
