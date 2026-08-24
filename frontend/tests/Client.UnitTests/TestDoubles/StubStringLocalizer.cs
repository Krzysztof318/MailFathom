// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Localization;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>
/// Stands in for the string table a running head resolves a <c>x:Uid</c> and an <see cref="IStringLocalizer"/> lookup
/// against.
/// </summary>
/// <remarks>
/// The real one reads the compiled resource map, which a unit-test host has none of. What a model owes it is the key
/// it asks for, so this answers with the words a test named and reports anything else as not found — which is what
/// lets a test assert that a key exists rather than that a word came back.
/// </remarks>
internal sealed class StubStringLocalizer : IStringLocalizer
{
    private readonly IReadOnlyDictionary<string, string> words;

    /// <summary>Initializes the stub with the words it can resolve.</summary>
    /// <param name="words">The keys the table holds, and what each one reads as.</param>
    public StubStringLocalizer(IReadOnlyDictionary<string, string> words) => this.words = words;

    /// <inheritdoc />
    public LocalizedString this[string name] =>
        this.words.TryGetValue(name, out var word)
            ? new LocalizedString(name, word)
            : new LocalizedString(name, name, resourceNotFound: true);

    /// <inheritdoc />
    public LocalizedString this[string name, params object[] arguments] => this[name];

    /// <inheritdoc />
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        this.words.Select(word => new LocalizedString(word.Key, word.Value));
}
