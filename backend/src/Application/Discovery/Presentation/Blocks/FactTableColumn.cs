// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>The closed catalogue of columns a fact table may compare values across.</summary>
/// <remarks>
/// <para>
/// A column names what a cell holds, and nothing else. It carries no heading, because a heading is words in somebody's
/// language and the client is localized: a producer that shipped the word "Amount" would have shipped an English screen
/// to a Polish reader. The client draws the heading for the column it was handed, which is only possible because the
/// set is closed.
/// </para>
/// <para>
/// It is a closed enumeration rather than a C# <see langword="enum" /> because a member carries its identity on the
/// wire and the kind of value its cells hold. The kind is what lets a client align a column, sort it, or refuse to —
/// and putting it in a lookup table beside the set would be a second place to keep in step. Being a struct,
/// <see langword="default" /> is reachable and names no column; it reports itself through <see cref="IsSpecified" />
/// and is refused by the converter and by the table.
/// </para>
/// </remarks>
[JsonConverter(typeof(FactTableColumnJsonConverter))]
public readonly record struct FactTableColumn
{
    private readonly string? identity;

    private FactTableColumn(string identity, FactTableValueKind valueKind)
    {
        this.identity = identity;
        this.ValueKind = valueKind;
    }

    #region What the row is about

    /// <summary>Gets the column naming what the row concerns — the matter, the document, or the item compared.</summary>
    public static FactTableColumn Subject { get; } = new("subject", FactTableValueKind.Text);

    /// <summary>Gets the column naming the person or organization the row concerns.</summary>
    public static FactTableColumn Party { get; } = new("party", FactTableValueKind.Text);

    /// <summary>Gets the column naming the document the row was read from.</summary>
    public static FactTableColumn Document { get; } = new("document", FactTableValueKind.Text);

    /// <summary>Gets the column holding whatever reference the correspondence itself uses — an order number, a case number, a ticket.</summary>
    public static FactTableColumn Reference { get; } = new("reference", FactTableValueKind.Text);

    #endregion

    #region What is being compared

    /// <summary>Gets the column holding a money amount, written as the correspondence wrote it, currency included.</summary>
    public static FactTableColumn Amount { get; } = new("amount", FactTableValueKind.Amount);

    /// <summary>Gets the column holding a counted quantity.</summary>
    public static FactTableColumn Quantity { get; } = new("quantity", FactTableValueKind.Number);

    /// <summary>Gets the column holding a term of an agreement — a period, a notice, a condition.</summary>
    public static FactTableColumn Term { get; } = new("term", FactTableValueKind.Text);

    /// <summary>Gets the column holding which revision of something the row is about.</summary>
    public static FactTableColumn Version { get; } = new("version", FactTableValueKind.Text);

    /// <summary>Gets the column holding where something stands.</summary>
    public static FactTableColumn Status { get; } = new("status", FactTableValueKind.Text);

    #endregion

    #region When

    /// <summary>Gets the column holding the date the row is about.</summary>
    public static FactTableColumn Date { get; } = new("date", FactTableValueKind.Date);

    #endregion

    /// <summary>Gets every column the catalogue holds.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<FactTableColumn> All { get; } =
    [
        Subject,
        Party,
        Document,
        Reference,
        Amount,
        Quantity,
        Term,
        Version,
        Status,
        Date,
    ];

    /// <summary>Gets whether this value names a catalogued column rather than the unusable struct default.</summary>
    public bool IsSpecified => this.identity is not null;

    /// <summary>Gets what kind of value the column's cells hold.</summary>
    public FactTableValueKind ValueKind { get; }

    /// <summary>Gets the identity the wire uses, which survives a rename of the C# member.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a column.</exception>
    public string Identity => this.identity
        ?? throw new InvalidOperationException("The value is the default of the struct and names no column.");

    /// <summary>Parses an identity read off the wire.</summary>
    /// <param name="identity">The identity to parse.</param>
    /// <param name="column">The catalogued column when the identity names one; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the identity names a catalogued column; otherwise <see langword="false" />.</returns>
    public static bool TryParse(string? identity, out FactTableColumn column)
    {
        column = default;

        if (string.IsNullOrWhiteSpace(identity))
        {
            return false;
        }

        var normalized = identity.Trim();

        column = All.FirstOrDefault(candidate => string.Equals(candidate.Identity, normalized, StringComparison.Ordinal));

        return column.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.identity ?? "(unspecified)";
}

/// <summary>Serializes <see cref="FactTableColumn" /> as the identity the catalogue publishes.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so a column is written as the name a
/// client keys its heading and its alignment by rather than as a position in a list that may be reordered.
/// </remarks>
public sealed class FactTableColumnJsonConverter : JsonConverter<FactTableColumn>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a catalogued column.</exception>
    public override FactTableColumn Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A fact-table column must be a JSON string, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(
        Utf8JsonWriter writer,
        FactTableColumn value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(SpecifiedOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a catalogued column.</exception>
    public override FactTableColumn ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        FactTableColumn value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(SpecifiedOrThrow(value));
    }

    private static FactTableColumn ParseOrThrow(string? identity) =>
        FactTableColumn.TryParse(identity, out var parsed)
            ? parsed
            : throw new JsonException("The value does not name a column this catalogue holds.");

    private static string SpecifiedOrThrow(FactTableColumn value) => value.IsSpecified
        ? value.Identity
        : throw new JsonException("A column cannot be written from the unspecified default of the struct.");
}
