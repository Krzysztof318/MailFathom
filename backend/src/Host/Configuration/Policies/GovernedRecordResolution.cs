// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>Resolves a governed record through the settings policies that govern it.</summary>
/// <remarks>
/// <para>
/// One order, written once, for every kind of record a policy governs. Each property resolves on its own, first hit
/// winning: the deployment's forced value, the organization's forced value, the record's own value, the
/// organization's default, the deployment's default, and whatever the build takes for a property nothing states.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0037-a-settings-policy-at-the-deployment-and-at-each-organization.md">ADR 0037</see>
/// § <em>How a value resolves</em> holds why the two halves of that order run in opposite directions.
/// </para>
/// <para>
/// Nothing here names a property. The properties are the ones the record's own type declares, read through
/// <see cref="SettingsPolicySection.Properties" />, so a property a later release adds to a record is resolved from
/// the build that adds it with nothing here edited. A property is a leaf of the record or a list: a block is not
/// resolved as a unit, and a list a layer states replaces the layer beneath it whole.
/// </para>
/// <para>
/// The record's own document is never changed. What comes back is a second document with the values that won
/// written over a copy of it, which a binder reads and nothing stores.
/// </para>
/// </remarks>
internal static class GovernedRecordResolution
{
    /// <summary>Resolves one record.</summary>
    /// <param name="section">The kind of record.</param>
    /// <param name="layers">What the policies governing the record say about its kind.</param>
    /// <param name="ownJson">The record's own document, as its row holds it.</param>
    /// <returns>The record as it is served, and where each of its properties came from.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A document that is not a JSON object resolves as one stating nothing and is handed back as it was written, so
    /// that the binder which reads the answer refuses it in its own words rather than this refusing it in others.
    /// </remarks>
    internal static ResolvedRecord Resolve(SettingsPolicySection section, SettingsPolicyLayers layers, string ownJson)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(ownJson);

        var own = ReadDocument(ownJson);
        var effective = own?.DeepClone().AsObject() ?? [];
        var builtIn = new Lazy<object>(() => Activator.CreateInstance(section.RecordType)!);

        ResolvedProperty[] properties =
        [
            .. section.Properties.All
                .Where(property => property.Shape != GovernablePropertyShape.Block)
                .OrderBy(property => property.Path, StringComparer.Ordinal)
                .Select(property => ResolveProperty(property, layers, own ?? [], effective, builtIn)),
        ];

        return new ResolvedRecord(own is null ? ownJson : effective.ToJsonString(), properties);
    }

    /// <summary>Reads a record's own document, or nothing where it is not a JSON object.</summary>
    /// <param name="json">The document as its row holds it.</param>
    /// <returns>The document, or <see langword="null" /> where it is not one.</returns>
    internal static JsonObject? ReadDocument(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ResolvedProperty ResolveProperty(
        GovernableProperty property,
        SettingsPolicyLayers layers,
        JsonObject own,
        JsonObject effective,
        Lazy<object> builtIn)
    {
        var withheldBy = layers.Deployment.Withholds(property)
            ? SettingsPolicyScope.Deployment
            : layers.Organization?.Withholds(property) == true
                ? SettingsPolicyScope.Organization
                : (SettingsPolicyScope?)null;

        // A column beside the record is no key of its document, so nothing in a document states it and a policy says
        // only who may change it.
        if (property.Accessors.Count == 0)
        {
            return new ResolvedProperty(property, SettingsValueSource.Record, EffectiveValue: null, withheldBy);
        }

        (SettingsValueSource Source, JsonNode? Stated)[] steps =
        [
            (SettingsValueSource.DeploymentForced, SettingsDocumentTree.Find(layers.Deployment.Forced, property.Path)),
            (SettingsValueSource.OrganizationForced, StatedBy(layers.Organization?.Forced, property)),
            (SettingsValueSource.Record, SettingsDocumentTree.Find(own, property.Path)),
            (SettingsValueSource.OrganizationDefault, StatedBy(layers.Organization?.Defaults, property)),
            (SettingsValueSource.DeploymentDefault, SettingsDocumentTree.Find(layers.Deployment.Defaults, property.Path)),
        ];

        if (steps.FirstOrDefault(step => step.Stated is not null) is not { Stated: { } value } won)
        {
            return new ResolvedProperty(property, SettingsValueSource.BuiltIn, BuiltInValueOf(property, builtIn), withheldBy);
        }

        if (won.Source != SettingsValueSource.Record)
        {
            SettingsDocumentTree.Replace(effective, property.Path, value);
        }

        return new ResolvedProperty(property, won.Source, value.DeepClone(), withheldBy);
    }

    private static JsonNode? StatedBy(JsonObject? statements, GovernableProperty property) =>
        statements is null ? null : SettingsDocumentTree.Find(statements, property.Path);

    /// <summary>Reads what the build takes for a property nothing states, where that is one value a reader can be shown.</summary>
    private static JsonValue? BuiltInValueOf(GovernableProperty property, Lazy<object> builtIn)
    {
        if (property.Shape != GovernablePropertyShape.Value)
        {
            return null;
        }

        return property.ReadFrom(builtIn.Value).Value switch
        {
            null => null,
            bool flag => JsonValue.Create(flag),
            string text => JsonValue.Create(text),
            IFormattable formattable => JsonValue.Create(formattable.ToString(format: null, CultureInfo.InvariantCulture)),
            var other => JsonValue.Create(other.ToString()),
        };
    }
}
