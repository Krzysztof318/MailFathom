// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Application.Preferences;

/// <summary>What a person chose the client to be painted in: one of the two themes, or whatever their machine is set to.</summary>
/// <remarks>
/// <para>
/// A closed enumeration rather than a C# <see langword="enum" /> because the name travels both ways across the client
/// endpoint and is stored as itself: it is what a preferences read answers with, what a write states, and what the
/// persisted document holds. A member rename must not change any of the three, and an ordinal would mean nothing to
/// the client reading the response.
/// </para>
/// <para>
/// The set is the client's own rather than a second opinion beside it. <c>theme/themeChoice.ts</c> offers exactly
/// these three, so a name this build does not publish is one no client could have chosen, and it is refused at the
/// boundary rather than stored for a screen that could not render it.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and is not a choice. It reports itself through
/// <see cref="IsSpecified" />, refuses to answer for a name, and is rejected by the converter below, so nothing
/// undeclared reaches a response or the stored document.
/// </para>
/// </remarks>
[JsonConverter(typeof(ClientThemeChoiceJsonConverter))]
public readonly record struct ClientThemeChoice
{
    private readonly string? name;

    private ClientThemeChoice(string name) => this.name = name;

    /// <summary>Gets the choice to follow whatever appearance the machine in front of the person is set to.</summary>
    /// <remarks>It is what an unset preference reads as, because somebody who has chosen nothing has not asked for either theme.</remarks>
    public static ClientThemeChoice System { get; } = new("system");

    /// <summary>Gets the choice to paint the client light whatever the machine is set to.</summary>
    public static ClientThemeChoice Light { get; } = new("light");

    /// <summary>Gets the choice to paint the client dark whatever the machine is set to.</summary>
    public static ClientThemeChoice Dark { get; } = new("dark");

    /// <summary>Gets every choice this build publishes.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<ClientThemeChoice> All { get; } = [System, Light, Dark];

    /// <summary>Gets whether this value names a published choice rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets the published name, which is what a client reads and what the stored document holds.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a choice.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a theme choice.");

    /// <summary>Reports the choice by its published name.</summary>
    /// <param name="name">The name to resolve.</param>
    /// <param name="choice">The choice the name publishes, or the struct default when no choice publishes it.</param>
    /// <returns><see langword="true" /> when the name is one this build publishes.</returns>
    public static bool TryParse(string? name, out ClientThemeChoice choice)
    {
        choice = All.FirstOrDefault(candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));

        return choice.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}

/// <summary>Serializes <see cref="ClientThemeChoice" /> as its published name, and refuses anything else.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so the client endpoint and the
/// persisted document are read and written the same way without either registering it. Refusing an undeclared name is
/// what keeps the stored set closed: a write naming a theme this build does not publish fails to bind and is answered
/// as a refused request rather than committed.
/// </remarks>
public sealed class ClientThemeChoiceJsonConverter : JsonConverter<ClientThemeChoice>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a published choice.</exception>
    public override ClientThemeChoice Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? ParseOrThrow(reader.GetString())
            : throw new JsonException("A theme choice is the name of one, which is a JSON string.");

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the struct default rather than a choice.</exception>
    public override void Write(Utf8JsonWriter writer, ClientThemeChoice value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(NameOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a published choice.</exception>
    public override ClientThemeChoice ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the struct default rather than a choice.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        ClientThemeChoice value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(NameOrThrow(value));
    }

    private static ClientThemeChoice ParseOrThrow(string? name) => ClientThemeChoice.TryParse(name, out var choice)
        ? choice
        : throw new JsonException("The value does not name a theme choice this build publishes.");

    private static string NameOrThrow(ClientThemeChoice value) => value.IsSpecified
        ? value.Name
        : throw new JsonException("The value is the default of the struct and does not name a theme choice.");
}
