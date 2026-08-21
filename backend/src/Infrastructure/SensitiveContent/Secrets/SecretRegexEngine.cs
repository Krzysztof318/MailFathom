// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Microsoft.Security.Utilities;

namespace MailFathom.Infrastructure.SensitiveContent.Secrets;

/// <summary>Runs the corpus's expressions for <see cref="SecretContentScanner" />, and bounds every match it makes.</summary>
/// <remarks>
/// <para>
/// A corpus entry carries its expression as a string, because that is how the detection engine declares one, and a
/// source-generated matcher is a compile-time artifact rather than a string. This is the piece that joins them: an
/// expression MailFathom compiled is found again by the pattern text it was built from, and anything the engine ships
/// its own expression for is compiled here instead of being forked.
/// </para>
/// <para>
/// <b>Every match is bounded.</b> The engine's own cache builds its expressions with no match timeout at all, which is
/// safe for the linear-time matcher it selects and not something to inherit for untrusted text on a hot path. Both
/// paths through this type carry <see cref="MatchTimeoutMilliseconds" />, so a single expression that meets input it
/// degrades on ends the operation rather than holding it. The scan budget an operator configures bounds the whole scan
/// on top of that; this bounds one expression within it.
/// </para>
/// <para>
/// The two paths use different matchers on purpose, and the choice was measured rather than preferred. MailFathom's
/// own corpus is derived from RE2 expressions, which carry no backreference and no nested quantifier for a backtracking
/// matcher to degrade on, and the generated matcher runs eight to eleven times faster than the linear-time one over the
/// text a mailbox actually produces the worst case with — a base64 attachment fragment or a long run of hexadecimal.
/// The engine's own patterns stay on the linear-time matcher it selects for them, because their expressions are the
/// package's to reason about rather than this repository's.
/// </para>
/// </remarks>
internal sealed class SecretRegexEngine
{
    /// <summary>The options every expression MailFathom compiles is built with.</summary>
    /// <remarks>
    /// <see cref="RegexOptions.ExplicitCapture" /> is what makes the <c>refine</c> group the only capture, so the
    /// groups a third-party expression uses for grouping do not become the region a finding covers.
    /// </remarks>
    public const RegexOptions MatchOptions = RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;

    /// <summary>How long one expression may spend on one input before the scan is refused.</summary>
    /// <remarks>
    /// A constant rather than a configured value, because it is baked into every generated matcher at compile time. It
    /// is far above what the whole corpus costs over the largest text a scan analyzes, so reaching it means an
    /// expression met input it degrades on rather than a machine under load.
    /// </remarks>
    public const int MatchTimeoutMilliseconds = 1_000;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(MatchTimeoutMilliseconds);

    private readonly FrozenDictionary<string, Regex> compiled;
    private readonly ConcurrentDictionary<(string Pattern, RegexOptions Options), Regex> adopted = new();

    /// <summary>Initializes the engine over the corpus a scanner was built with.</summary>
    /// <param name="rules">The corpus entries, of which the compiled ones are indexed by their pattern text.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rules" /> is <see langword="null" />.</exception>
    public SecretRegexEngine(IEnumerable<SecretRuleDefinition> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        this.compiled = rules
            .Where(rule => rule.Expression is not null)
            .DistinctBy(rule => rule.Expression!.ToString(), StringComparer.Ordinal)
            .ToFrozenDictionary(rule => rule.Expression!.ToString(), rule => rule.Expression!, StringComparer.Ordinal);
    }

    /// <summary>Finds every occurrence of a registered expression, narrowed to the region a finding covers.</summary>
    /// <param name="input">The text to search, which is untrusted.</param>
    /// <param name="pattern">The pattern text the expression was registered with.</param>
    /// <param name="options">The options it was registered with, or <see langword="null" /> for the engine's own.</param>
    /// <param name="captureGroup">The group to report in place of the whole match, where the expression declares one.</param>
    /// <returns>The region of each occurrence, in the order the text carries them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="RegexMatchTimeoutException">Thrown when one expression exceeds <see cref="MatchTimeoutMilliseconds" />.</exception>
    public IEnumerable<Group> Matches(
        string input,
        string pattern,
        RegexOptions? options = null,
        string? captureGroup = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(pattern);

        return Collect(this.MatcherFor(pattern, options), input, captureGroup);
    }

    /// <summary>Finds the matcher an expression will run under, whichever of the two paths supplies it.</summary>
    /// <param name="pattern">The pattern text a registered expression was declared with.</param>
    /// <param name="options">The options that pattern was registered with, or <see langword="null" /> for the engine's own.</param>
    /// <returns>The matcher, which always carries <see cref="MatchTimeoutMilliseconds" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pattern" /> is <see langword="null" />.</exception>
    public Regex MatcherFor(string pattern, RegexOptions? options)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        return this.compiled.TryGetValue(pattern, out var generated)
            ? generated
            : this.adopted.GetOrAdd(
                (pattern, options ?? RegexDefaults.DefaultOptions),
                // The engine writes a named group the Python way, which .NET rejects outright, and its own cache
                // rewrites it on the way in. Compiling one of its patterns here means doing the same.
                key => new Regex(key.Pattern.Replace("?P<", "?<", StringComparison.Ordinal), key.Options, MatchTimeout));
    }

    private static IEnumerable<Group> Collect(Regex matcher, string input, string? captureGroup)
    {
        foreach (Match match in matcher.Matches(input))
        {
            yield return Region(match, captureGroup);
        }
    }

    private static Group Region(Match match, string? captureGroup)
    {
        if (captureGroup is null)
        {
            return match;
        }

        var capture = match.Groups[captureGroup];

        return capture.Success ? capture : match;
    }
}
