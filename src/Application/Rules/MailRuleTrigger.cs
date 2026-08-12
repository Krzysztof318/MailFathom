// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Application.Rules;

/// <summary>Names one automatic occasion on which a rule takes part in a pass.</summary>
/// <remarks>
/// <para>
/// A closed enumeration rather than a C# <see langword="enum" />, because the name is the identity: it is what an
/// operator writes under a rule's <c>Triggers</c> key and the form a rule set's revision is derived from. An enum
/// member's ordinal would mean nothing outside this assembly, and its member name would change with a rename that the
/// configuration surface and the derived identity both have to survive.
/// </para>
/// <para>
/// One member today, and the set is closed rather than final: a scheduled pass was refused for now rather than forever,
/// so a second occasion is a member to append. What is deliberately not a member is a run somebody asked for. That is
/// the request itself rather than an occasion a rule opts into, so it reaches every rule of the set;
/// <see cref="MailRuleReach" /> is where the two are told apart.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and names no trigger. <see cref="IsSpecified" /> reports
/// that, <see cref="TryParseName" /> never produces one, and <see cref="MailRule.Create" /> refuses one.
/// </para>
/// </remarks>
[JsonConverter(typeof(MailRuleTriggerJsonConverter))]
public readonly record struct MailRuleTrigger
{
    private readonly string? name;

    private MailRuleTrigger(string name) => this.name = name;

    /// <summary>Gets the trigger that runs a rule over a message the account's synchronization run has just committed.</summary>
    /// <remarks>
    /// Named after the moment rather than after the transport, because <c>Push</c> already names an account's
    /// synchronization mode and a rule is unaffected by whether its account is watched or polled: mail committed by a
    /// polled run reaches this trigger exactly as mail committed by a watched one does.
    /// </remarks>
    public static MailRuleTrigger Arrival { get; } = new("Arrival");

    /// <summary>Gets every trigger a rule may declare.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<MailRuleTrigger> All { get; } = [Arrival];

    /// <summary>Gets the triggers a rule that declares none takes part in.</summary>
    /// <remarks>
    /// Arrival, which is what every rule written before the key existed already did, so an absent key leaves a rule set
    /// meaning what it meant. It is a value of its own rather than <see cref="All" /> because the two answer different
    /// questions and would part company the moment a second trigger exists: a rule that says nothing would still run on
    /// arrival alone, rather than joining every occasion added since it was written.
    /// </remarks>
    public static IReadOnlyList<MailRuleTrigger> WhenNoneDeclared { get; } = [Arrival];

    /// <summary>Gets whether this value names a trigger rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets the name the trigger is declared and reported under.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a trigger.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and names no mail rule trigger.");

    /// <summary>Parses a declared name back into the trigger it names.</summary>
    /// <param name="name">The name read from a configuration key or a serialized document.</param>
    /// <param name="trigger">The trigger when the name is one this set holds; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the name is a declared trigger; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Surrounding whitespace is ignored and case is not compared, which is how the configuration binder already reads
    /// every other closed vocabulary this deployment declares. A name this set does not hold is refused rather than
    /// reconstructed, because a trigger nothing runs is unknown rather than new.
    /// </remarks>
    public static bool TryParseName(string? name, out MailRuleTrigger trigger)
    {
        trigger = default;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var declaredName = name.Trim();

        // No declared trigger is the struct default, so an unmatched name yields the unspecified value the caller
        // already receives when parsing fails.
        trigger = All.FirstOrDefault(candidate => StringComparer.OrdinalIgnoreCase.Equals(candidate.Name, declaredName));

        return trigger.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}

/// <summary>Serializes <see cref="MailRuleTrigger" /> as its name.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so every serializer that meets the
/// value uses it without per-call registration. The JSON form is the name for the same reason the value object exists:
/// it is the identity an operator already writes in a configuration file, and an ordinal would change meaning silently
/// if the declared set were ever reordered.
/// </remarks>
public sealed class MailRuleTriggerJsonConverter : JsonConverter<MailRuleTrigger>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a declared trigger.</exception>
    public override MailRuleTrigger Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A mail rule trigger must be a JSON string, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(
        Utf8JsonWriter writer,
        MailRuleTrigger value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(NameOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a declared trigger.</exception>
    public override MailRuleTrigger ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        MailRuleTrigger value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(NameOrThrow(value));
    }

    private static MailRuleTrigger ParseOrThrow(string? name)
    {
        if (!MailRuleTrigger.TryParseName(name, out var trigger))
        {
            throw new JsonException($"'{name}' does not name a declared mail rule trigger.");
        }

        return trigger;
    }

    private static string NameOrThrow(MailRuleTrigger trigger) => trigger.IsSpecified
        ? trigger.Name
        : throw new JsonException("An unspecified mail rule trigger cannot be serialized.");
}
