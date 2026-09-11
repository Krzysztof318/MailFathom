// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Application.Preferences;

/// <summary>Which of the three renderings a message opens on.</summary>
/// <remarks>
/// <para>
/// A closed enumeration rather than a C# <see langword="enum" />, for the reason <see cref="ClientThemeChoice" /> is one:
/// the name travels both ways across the client endpoint and is stored as itself, so a member rename must not change any
/// of the three and an ordinal would mean nothing to the client reading the response.
/// </para>
/// <para>
/// Three members rather than the two this preference carried before. The reduced document and the sender's own markup are
/// what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0024-rendering-mail-in-the-client-as-a-closed-document-tree.md">ADR 0024</see>
/// named; the cleaned one is the reduced document with what the sender wrapped it in dropped, derived per open by a model
/// that answers in block indices and never in text. It is a choice beside the other two rather than a switch on top of one,
/// because a reader picks one rendering and the screen draws it.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and is not a choice. It reports itself through
/// <see cref="IsSpecified" />, refuses to answer for a name, and is rejected by the converter below, so nothing undeclared
/// reaches a response or the stored document.
/// </para>
/// </remarks>
[JsonConverter(typeof(ClientMessageViewJsonConverter))]
public readonly record struct ClientMessageView
{
    private readonly string? name;

    private ClientMessageView(string name) => this.name = name;

    /// <summary>Gets the choice to draw the closed document tree the service reduced the body to.</summary>
    /// <remarks>What an unset preference reads as, because it is what this client has always drawn and costs no provider call.</remarks>
    public static ClientMessageView Reduced { get; } = new("reduced");

    /// <summary>Gets the choice to draw that same tree with the blocks a cleaning dropped absent.</summary>
    public static ClientMessageView Cleaned { get; } = new("cleaned");

    /// <summary>Gets the choice to draw the sender's own markup inline, with everything that runs or reports removed.</summary>
    public static ClientMessageView EmbeddedHtml { get; } = new("embeddedHtml");

    /// <summary>Gets every choice this build publishes.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<ClientMessageView> All { get; } = [Reduced, Cleaned, EmbeddedHtml];

    /// <summary>Gets whether this value names a published choice rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets the published name, which is what a client reads and what the stored document holds.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a choice.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a message view.");

    /// <summary>Reports the choice by its published name.</summary>
    /// <param name="name">The name to resolve.</param>
    /// <param name="view">The choice the name publishes, or the struct default when no choice publishes it.</param>
    /// <returns><see langword="true" /> when the name is one this build publishes.</returns>
    public static bool TryParse(string? name, out ClientMessageView view)
    {
        view = All.FirstOrDefault(candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));

        return view.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}

/// <summary>Serializes <see cref="ClientMessageView" /> as its published name, and refuses anything else.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so the client endpoint and the persisted
/// document are read and written the same way without either registering it. Refusing an undeclared name is what keeps the
/// stored set closed: a write naming a rendering this build does not publish fails to bind and is answered as a refused
/// request rather than committed.
/// </remarks>
public sealed class ClientMessageViewJsonConverter : JsonConverter<ClientMessageView>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a published choice.</exception>
    public override ClientMessageView Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? ParseOrThrow(reader.GetString())
            : throw new JsonException("A message view is the name of one, which is a JSON string.");

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the struct default rather than a choice.</exception>
    public override void Write(Utf8JsonWriter writer, ClientMessageView value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(NameOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a published choice.</exception>
    public override ClientMessageView ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the struct default rather than a choice.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        ClientMessageView value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(NameOrThrow(value));
    }

    private static ClientMessageView ParseOrThrow(string? name) => ClientMessageView.TryParse(name, out var view)
        ? view
        : throw new JsonException("The value does not name a message view this build publishes.");

    private static string NameOrThrow(ClientMessageView value) => value.IsSpecified
        ? value.Name
        : throw new JsonException("The value is the default of the struct and does not name a message view.");
}
