// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>Which step of the resolution order supplied a governed property's effective value.</summary>
/// <remarks>
/// <para>
/// A closed enumeration rather than a C# <see langword="enum" /> because the name travels and the value carries data:
/// the name is what a read of a record answers with as <c>source</c> on the client and the administrative surface
/// alike and what <c>mfctl user show --effective</c> prints beside a setting, so a member rename must not change it;
/// and a forcing step knows whose policy forces, which a lookup beside an enum would let drift from the member.
/// </para>
/// <para>
/// Declared in the order a value resolves in, first hit winning. The two halves run in opposite directions on
/// purpose: a force is the wider scope overruling the narrower one, so the deployment's is read first, and a default
/// is the narrower scope knowing its own people better, so the organization's is.
/// </para>
/// <para>
/// The struct default is reachable, as it is for any struct, and names no step; <see cref="Name" /> and the converter
/// refuse it.
/// </para>
/// </remarks>
[JsonConverter(typeof(SettingsValueSourceJsonConverter))]
internal readonly record struct SettingsValueSource
{
    private readonly string? name;

    private SettingsValueSource(string name, SettingsPolicyScope? forcedBy = null)
    {
        this.name = name;
        this.ForcedBy = forcedBy;
    }

    /// <summary>Gets the step at which the deployment's policy forces the value.</summary>
    internal static SettingsValueSource DeploymentForced { get; } = new("DeploymentForced", SettingsPolicyScope.Deployment);

    /// <summary>Gets the step at which the organization's policy forces the value.</summary>
    internal static SettingsValueSource OrganizationForced { get; } = new("OrganizationForced", SettingsPolicyScope.Organization);

    /// <summary>Gets the step at which the record states the value itself.</summary>
    internal static SettingsValueSource Record { get; } = new("Record");

    /// <summary>Gets the step at which the record states nothing and the organization's policy supplies a default.</summary>
    internal static SettingsValueSource OrganizationDefault { get; } = new("OrganizationDefault");

    /// <summary>Gets the step at which the record and the organization state nothing and the deployment's policy supplies a default.</summary>
    internal static SettingsValueSource DeploymentDefault { get; } = new("DeploymentDefault");

    /// <summary>Gets the step at which nothing states the value, so it is whatever the build takes for an unstated one.</summary>
    internal static SettingsValueSource BuiltIn { get; } = new("BuiltIn");

    /// <summary>Gets every step of the resolution order.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs, and in the order a value resolves in.</remarks>
    internal static IReadOnlyList<SettingsValueSource> All { get; } =
        [DeploymentForced, OrganizationForced, Record, OrganizationDefault, DeploymentDefault, BuiltIn];

    /// <summary>Gets the scope whose policy forces a value this step supplied, or <see langword="null" /> where the step is not a force.</summary>
    internal SettingsPolicyScope? ForcedBy { get; }

    /// <summary>Gets whether this value names a step rather than the unusable struct default.</summary>
    internal bool IsSpecified => this.name is not null;

    /// <summary>Gets the published name, which is what a read answers with and what an operator reads.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a step.</exception>
    internal string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a step of the resolution order.");

    /// <summary>Reports the step by its published name.</summary>
    /// <param name="name">The name to resolve.</param>
    /// <param name="source">The step the name publishes, or the struct default when no step publishes it.</param>
    /// <returns><see langword="true" /> when the name is one this build publishes.</returns>
    internal static bool TryParse(string? name, out SettingsValueSource source)
    {
        source = All.FirstOrDefault(candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));

        return source.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}

/// <summary>Serializes <see cref="SettingsValueSource" /> as its published name.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so every serializer that meets the
/// value uses it without per-call registration. The record responses map the step to its name before the value
/// crosses the transport, and the converter is what keeps a serializer that ever met one directly from writing the
/// struct as an object of its members.
/// </remarks>
internal sealed class SettingsValueSourceJsonConverter : JsonConverter<SettingsValueSource>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a published step.</exception>
    public override SettingsValueSource Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A settings value source must be a JSON string, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(Utf8JsonWriter writer, SettingsValueSource value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(NameOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a published step.</exception>
    public override SettingsValueSource ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, SettingsValueSource value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(NameOrThrow(value));
    }

    private static SettingsValueSource ParseOrThrow(string? name)
    {
        if (!SettingsValueSource.TryParse(name, out var source))
        {
            throw new JsonException($"'{name}' is not a settings value source this build publishes.");
        }

        return source;
    }

    private static string NameOrThrow(SettingsValueSource value) => value.IsSpecified
        ? value.Name
        : throw new JsonException("The value is the default of the struct and does not name a step of the resolution order.");
}
