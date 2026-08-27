// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Configuration;

/// <summary>One change a configuration write applies: a setting given a value, or a setting the document stops carrying.</summary>
/// <remarks>
/// <para>
/// The change is stated in an ordinary colon-delimited configuration key and an ordinary configuration value, which is
/// the whole of the vocabulary. A persisted setting is the same key the file beneath it would have written, so an edit
/// that spoke a second language — a JSON pointer, a document fragment, a merge directive — would give the deployment
/// two ways to name one setting and nothing able to say which of them a later reader meant.
/// </para>
/// <para>
/// An index is a path segment like any other: <c>Rules:1:Name</c> reaches the second declared rule, because the
/// persisted document writes an array element as an object keyed by its own index. Nothing here interprets the segment;
/// the store that holds the document decides how a segment becomes a property, and the binder decides whether
/// <c>Rules:second:Name</c> was a position at all.
/// </para>
/// <para>
/// Removal is a change of its own rather than a value of <see langword="null" /> because the persisted layer is sparse:
/// a setting the document does not carry is inherited from the source beneath it, while a setting carrying no value
/// shadows that source with nothing. Only the first of those undoes a persisted setting, so only the first is what an
/// operator asking to stop persisting one means.
/// </para>
/// </remarks>
public sealed record ConfigurationEdit
{
    /// <summary>How long a configuration path may be.</summary>
    /// <remarks>
    /// A path is a sequence of section and property names, and the longest a MailFathom setting actually reaches is a
    /// secret nested inside an account's own block. The bound is generous against that, and what it refuses is a caller
    /// that has lost track of the path it is asking for, before the process pays for expanding it. It is not a bound on
    /// how deep the document a path produces is nested — a store writes one object per segment, and this admits far
    /// more segments than a JSON reader admits levels — so a document nested past what the reader accepts is refused
    /// where it is measured rather than here.
    /// </remarks>
    public const int MaximumPathLength = 512;

    /// <summary>How long a configuration value may be.</summary>
    /// <remarks>
    /// A setting is something an operator states rather than a payload they upload, so this bound refuses a caller that
    /// has lost track of what it is asking for before the value is expanded into a candidate document — the document's
    /// own ceiling is enforced where it is persisted, and reaching that one by composing a document out of megabytes
    /// first is the cost this exists to avoid.
    /// </remarks>
    public const int MaximumValueLength = 8 * 1024;

    private ConfigurationEdit(string path, string? value)
    {
        this.Path = path;
        this.Value = value;
    }

    /// <summary>Gets the colon-delimited configuration path the change reaches.</summary>
    public string Path { get; }

    /// <summary>Gets the value the setting takes, or <see langword="null" /> when the change removes the setting.</summary>
    /// <remarks><see cref="RemovesTheSetting" /> is what a reader asks; the property is nullable because a removal has no value to carry.</remarks>
    public string? Value { get; }

    /// <summary>Gets whether the change stops the document carrying the setting, leaving it inherited from below.</summary>
    public bool RemovesTheSetting => this.Value is null;

    /// <summary>States that a setting takes a value.</summary>
    /// <param name="path">The colon-delimited configuration path.</param>
    /// <param name="value">The configuration value, which may be empty because an empty string is a value an operator can mean.</param>
    /// <returns>The change.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path" /> is not a configuration key, or when either the path or <paramref name="value" /> carries a NUL character.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value" /> is <see langword="null" />, which is <see cref="Removing" /> rather than a value.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="path" /> is longer than <see cref="MaximumPathLength" />, or <paramref name="value" /> longer than <see cref="MaximumValueLength" />.</exception>
    /// <remarks>
    /// A NUL is refused here rather than left to the commit because it is the one character a candidate can carry that
    /// composes into valid JSON and can never be stored: PostgreSQL's <c>jsonb</c> holds text, and text in PostgreSQL
    /// has no NUL, so the change would compose, validate, and then be refused by the server on every attempt. Answering
    /// it at the surface that stated the change names the character; answering it at the commit could only name a state
    /// the server gave. Both halves of an edit are refused for it, because a segment becomes a property name and a key
    /// is text exactly as a value is.
    /// </remarks>
    public static ConfigurationEdit SetTo(string path, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, MaximumValueLength, nameof(value));

        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The configuration value carries a NUL character, which no PostgreSQL text value can hold, so the document composed from it could never be persisted.",
                nameof(value));
        }

        return new ConfigurationEdit(Validated(path), value);
    }

    /// <summary>States that the document stops carrying a setting, so the source beneath the layer supplies it again.</summary>
    /// <param name="path">The colon-delimited configuration path.</param>
    /// <returns>The change.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path" /> is not a configuration key, or carries a NUL character.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="path" /> is longer than <see cref="MaximumPathLength" />.</exception>
    public static ConfigurationEdit Removing(string path) => new(Validated(path), value: null);

    /// <summary>Refuses a path that is not a configuration key, which is a caller's mistake rather than a rejected write.</summary>
    /// <remarks>
    /// A key with an empty segment addresses nothing: the configuration binder splits on the colon and would be handed
    /// a nameless property, and the document would grow a member no reader can ever reach. Guarding it here rather than
    /// at the write is what keeps every surface that composes an edit — a command line, an administrative request —
    /// judging the shape by one rule instead of each carrying its own.
    /// </remarks>
    private static string Validated(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(path.Length, MaximumPathLength, nameof(path));

        // The same character the value guard refuses, and for the same reason: a segment becomes a JSON property name,
        // and PostgreSQL refuses a NUL in a key exactly as it refuses one in a string. Neither guard above sees it —
        // a NUL is not white space and a segment carrying one is not empty.
        if (path.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The configuration path carries a NUL character, which no PostgreSQL text value can hold, so the document composed from it could never be persisted.",
                nameof(path));
        }

        if (path.Split(':').Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                $"The configuration path '{path}' carries an empty segment, so it names no setting.",
                nameof(path));
        }

        return path;
    }
}
