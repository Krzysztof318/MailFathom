// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Nodes;
using MailFathom.Application.Preferences;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>What one scope's settings policy says about one kind of record, read out of the document that scope stores.</summary>
/// <remarks>
/// <para>
/// The three statements of one section, as a resolution reads them: the defaults and the forced values as the sparse
/// documents they were written as, and the editing restriction as its mode and its list. A scope that stores no
/// policy, and a policy that leaves the section out, are both <see cref="Nothing" />.
/// </para>
/// <para>
/// A document is read only once this build has judged it as a policy, by the judgement a write is held to. One it
/// refuses in any part is not read at all, because a force this build cannot bind is not one it can apply, and
/// applying the rest would serve a record on half of what governs it.
/// </para>
/// </remarks>
internal sealed class SettingsPolicyStatements
{
    private const string DefaultsKey = "Defaults";
    private const string ForcedKey = "Forced";
    private const string EditingKey = "Editing";
    private const string EditingModeKey = "Mode";
    private const string EditingPropertiesKey = "Properties";

    private SettingsPolicyStatements(
        JsonObject defaults,
        JsonObject forced,
        SettingsPolicyEditingMode editingMode,
        IReadOnlyList<string> editingPaths)
    {
        this.Defaults = defaults;
        this.Forced = forced;
        this.EditingMode = editingMode;
        this.EditingPaths = editingPaths;
    }

    /// <summary>Gets the statements of a scope that says nothing about the record.</summary>
    internal static SettingsPolicyStatements Nothing { get; } =
        new([], [], SettingsPolicyEditingMode.AllExcept, []);

    /// <summary>Gets the defaults, as a sparse document in the shape of the governed record.</summary>
    internal JsonObject Defaults { get; }

    /// <summary>Gets the forced values, as a sparse document in the shape of the governed record.</summary>
    internal JsonObject Forced { get; }

    /// <summary>Gets what the list of the editing restriction means.</summary>
    internal SettingsPolicyEditingMode EditingMode { get; }

    /// <summary>Gets the property paths the editing restriction lists.</summary>
    internal IReadOnlyList<string> EditingPaths { get; }

    /// <summary>Reads what a stored policy says about one kind of record.</summary>
    /// <param name="policyJson">The policy as its row holds it.</param>
    /// <param name="section">The kind of record asked about.</param>
    /// <returns>The statements, or <see langword="null" /> when this build does not judge the document as a settings policy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    internal static SettingsPolicyStatements? Read(string policyJson, SettingsPolicySection section)
    {
        ArgumentNullException.ThrowIfNull(policyJson);
        ArgumentNullException.ThrowIfNull(section);

        if (SettingsPolicyCandidate.Judge(policyJson).Json is not { } judged)
        {
            return null;
        }

        if (SettingsDocumentTree.Find(JsonNode.Parse(judged)!.AsObject(), section.Name) is not JsonObject stated)
        {
            return Nothing;
        }

        var editing = SettingsDocumentTree.Find(stated, EditingKey) as JsonObject;

        return new SettingsPolicyStatements(
            RecordStatementsOf(stated, DefaultsKey),
            RecordStatementsOf(stated, ForcedKey),
            editing is not null
            && SettingsDocumentTree.Find(editing, EditingModeKey) is JsonValue mode
            && Enum.TryParse<SettingsPolicyEditingMode>(mode.GetValue<string>(), ignoreCase: true, out var chosen)
                ? chosen
                : SettingsPolicyEditingMode.AllExcept,
            editing is not null && SettingsDocumentTree.Find(editing, EditingPropertiesKey) is JsonArray listed
                ? [.. listed.Select(path => path!.GetValue<string>())]
                : []);
    }

    /// <summary>Reports whether the editing restriction withholds a property from the person the record is about.</summary>
    /// <param name="property">The property asked about.</param>
    /// <returns><see langword="true" /> when this scope does not leave the property to the person to change.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="property" /> is <see langword="null" />.</exception>
    /// <remarks>A listed path naming a block covers every property beneath it, in either mode.</remarks>
    internal bool Withholds(GovernableProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        var listed = this.EditingPaths.Any(path =>
            property.Path.Equals(path, StringComparison.OrdinalIgnoreCase)
            || property.Path.StartsWith($"{path}:", StringComparison.OrdinalIgnoreCase));

        return this.EditingMode == SettingsPolicyEditingMode.AllExcept ? listed : !listed;
    }

    /// <summary>Reads the defaults or the forced values a section states, leaving out the one statement that is not about the record.</summary>
    /// <remarks>
    /// A forced client preference is written beside the record's own properties and is no key of the record, so a
    /// resolution that carried it into the effective document would hand the record's binder a name it refuses.
    /// </remarks>
    private static JsonObject RecordStatementsOf(JsonObject section, string statement)
    {
        if (SettingsDocumentTree.Find(section, statement) is not JsonObject stated)
        {
            return [];
        }

        var statements = stated.DeepClone().AsObject();

        foreach (var name in statements
            .Select(member => member.Key)
            .Where(name => name.Equals(nameof(ClientPreferences), StringComparison.OrdinalIgnoreCase))
            .ToArray())
        {
            statements.Remove(name);
        }

        return statements;
    }
}
