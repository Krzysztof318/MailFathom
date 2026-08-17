// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Output;

/// <summary>One record read on its own, as the values it carries under the labels that name them.</summary>
/// <remarks>
/// <para>
/// The shape a command reaches for when it prints one thing rather than a listing of them. It holds labels and values
/// and no alignment: how far the values are set from the labels follows from the longest label, which is
/// <see cref="CliRenderer" />'s to work out and was previously padded by hand in every command that printed a record.
/// </para>
/// <para>
/// The label is what colour marks here, and it is marked so the value beside it reads as the answer rather than as part
/// of one run-on line. Nothing else in a record is coloured, because nothing else in one is a state.
/// </para>
/// </remarks>
internal sealed class CliDetails
{
    private readonly List<CliDetail> details = [];

    /// <summary>Gets the labelled values, in the order they are read.</summary>
    internal IReadOnlyList<CliDetail> Details => this.details;

    /// <summary>Adds one label and the single value under it.</summary>
    /// <param name="label">What names the value, written without a trailing colon.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal void Add(string label, string value)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(value);

        this.details.Add(new CliDetail(label, [value]));
    }

    /// <summary>Adds one label and every value under it.</summary>
    /// <param name="label">What names the values, written without a trailing colon.</param>
    /// <param name="values">The values, each drawn on its own line under the one label.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A label carrying several values — the addresses one person is reached at — states them once rather than
    /// repeating the label, which is the shape an operator reads them in and the reason a value is a list here at all.
    /// </remarks>
    internal void Add(string label, IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(values);

        this.details.Add(new CliDetail(label, values));
    }
}

/// <summary>One label and everything read under it.</summary>
/// <param name="Label">What names the values.</param>
/// <param name="Values">The values, each drawn on its own line.</param>
internal sealed record CliDetail(string Label, IReadOnlyList<string> Values);
