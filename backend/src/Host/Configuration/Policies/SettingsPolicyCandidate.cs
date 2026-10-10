// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Application.Preferences;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Settings;
using MailFathom.Infrastructure.Policies;
using Microsoft.Extensions.Configuration.Json;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>A settings policy somebody saved, judged on its own before anything is committed.</summary>
/// <remarks>
/// <para>
/// This is the half of the judgement that needs no record: every path names a property the governed record carries,
/// every value binds as that property does, and the class of each property admits what is said about it. A candidate
/// that fails any of it is refused whole, with one sentence per fault, because a path that names nothing would
/// otherwise be a rule that silently governs nothing.
/// </para>
/// <para>
/// A value is bound by the configuration binder a record is bound by, strictly, as the type the governed record binds
/// as — so a statement is a sparse record, and a wrong type inside it is the refusal a record would get. What the
/// binder cannot refuse is asked of each stated value beside it: a number no member of an enumeration carries, a rule
/// the property declares for itself, a written name the record's own rule does not know, and, of each entry of a
/// stated list, the rules that entry declares for itself or the record asks of that entry alone. A rule the record's
/// validator asks of the record as a whole
/// is not asked here — that delivery which is enabled names a host, and equally a bound that validator checks on one
/// property beside the rest — because a statement is not a record and that validator has nothing whole to read:
/// forcing one property of a block says nothing about its siblings, which each record states for itself.
/// </para>
/// <para>
/// Every fault is reported rather than the first, for the reason a record's binder reports them all: whoever is
/// correcting a policy one sentence at a time learns about the next only by saving it again. A value is repeated
/// only where the record's own rule quotes it — a language, a zone, a recording level somebody misspelled, the alias
/// a folder was given — and never otherwise, because a forced list of trusted senders is somebody's addresses; a key somebody wrote is
/// repeated only where it is shaped like a setting's name, so a refusal is MailFathom's own words rather than the
/// document's.
/// </para>
/// </remarks>
internal sealed class SettingsPolicyCandidate
{
    private const string DefaultsKey = "Defaults";
    private const string ForcedKey = "Forced";
    private const string EditingKey = "Editing";
    private const string EditingModeKey = "Mode";
    private const string EditingPropertiesKey = "Properties";
    private const string ClientPreferencesKey = nameof(ClientPreferences);

    private const string NotAPolicy =
        "The saved policy is not a JSON object stating each name once, so nothing was written. A settings policy is an object holding a Users section, a MailAccounts section, or both.";

    /// <summary>Refuses a name written twice in one object, which <see cref="JsonNode" /> would otherwise read as the last of the two.</summary>
    private static readonly JsonDocumentOptions StrictDocument = new() { AllowDuplicateProperties = false };

    private static readonly string[] ClientPreferenceNames =
    [
        .. typeof(ClientPreferences)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(preference => preference.Name),
    ];

    private readonly List<string> refusals = [];

    private readonly HashSet<string> mailAccountValues = new(StringComparer.Ordinal);

    private SettingsPolicyCandidate()
    {
    }

    /// <summary>Gets the policy as it would be committed, or <see langword="null" /> where it was refused.</summary>
    internal string? Json { get; private set; }

    /// <summary>Gets one sentence per fault, each naming what to correct; empty where the policy may be committed.</summary>
    internal IReadOnlyList<string> Refusals => this.refusals;

    /// <summary>Gets every default and forced value the policy states for a mail account, each as its statement, its property, and its value.</summary>
    /// <remarks>
    /// What comparing two policies needs and their documents do not give: a property is named here by the record's own
    /// path whatever case its keys were written in, so one statement reads the same however it was typed. An editing
    /// restriction is not among them, because it moves no value an account is served.
    /// </remarks>
    internal IReadOnlySet<string> MailAccountValues => this.mailAccountValues;

    /// <summary>Judges a saved policy on its own.</summary>
    /// <param name="documentJson">The policy as it was saved.</param>
    /// <returns>The candidate, carrying either the document to commit or what has to change first.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="documentJson" /> is <see langword="null" />.</exception>
    internal static SettingsPolicyCandidate Judge(string documentJson)
    {
        ArgumentNullException.ThrowIfNull(documentJson);

        var candidate = new SettingsPolicyCandidate();

        // Measured as it was written before anything parses it, so an absurd body is never copied and walked; the
        // rule itself is the second measurement, of the rendering the column stores.
        if (Encoding.UTF8.GetByteCount(documentJson) > SettingsPolicyDocument.MaximumOctets)
        {
            return candidate.Refuse(PastTheCeiling);
        }

        JsonObject? policy;

        try
        {
            policy = JsonNode.Parse(documentJson, documentOptions: StrictDocument) as JsonObject;
        }
        catch (JsonException)
        {
            // The parser's own sentence quotes where it stopped, which is a fragment of what somebody typed.
            return candidate.Refuse(NotAPolicy);
        }

        if (policy is null)
        {
            return candidate.Refuse(NotAPolicy);
        }

        // Measured on the rendering that is committed rather than on what was typed, because the two differ in
        // length: the rendering escapes what the typed form may carry literally, and the store refuses by the first.
        var rendered = policy.ToJsonString();

        if (RootSettingsCommitRules.PersistedOctetsOf(rendered) > SettingsPolicyDocument.MaximumOctets)
        {
            return candidate.Refuse(PastTheCeiling);
        }

        foreach (var (name, section) in candidate.MembersOf(policy, "The policy"))
        {
            if (SettingsPolicySection.All.FirstOrDefault(known => Names(name, known.Name)) is { } known)
            {
                candidate.JudgeSection(known, section);
            }
            else
            {
                candidate.refusals.Add(
                    $"The policy names {Repeatable(name)}, which is not a section of one. A settings policy holds {SettingsPolicySection.Users.Name}, {SettingsPolicySection.MailAccounts.Name}, or both.");
            }
        }

        if (candidate.refusals.Count == 0)
        {
            candidate.Json = rendered;
        }

        return candidate;
    }

    private static string PastTheCeiling =>
        $"The saved policy is past the {SettingsPolicyDocument.MaximumOctets} octets MailFathom reads a settings policy from, so nothing was written. A policy states defaults and forced values rather than carrying a payload.";

    private SettingsPolicyCandidate Refuse(string refusal)
    {
        this.refusals.Add(refusal);

        return this;
    }

    private void JudgeSection(SettingsPolicySection section, JsonNode? stated)
    {
        if (stated is not JsonObject statements)
        {
            this.refusals.Add(
                $"{section.Name} is a section of the policy, so it is an object holding {DefaultsKey}, {ForcedKey}, {EditingKey}, or any of them.");

            return;
        }

        foreach (var (name, value) in this.MembersOf(statements, section.Name))
        {
            if (Names(name, DefaultsKey) || Names(name, ForcedKey))
            {
                this.JudgeStatement(section, Names(name, ForcedKey) ? ForcedKey : DefaultsKey, value);
            }
            else if (Names(name, EditingKey))
            {
                this.JudgeEditing(section, value);
            }
            else
            {
                this.refusals.Add(
                    $"{section.Name} names {Repeatable(name)}, which is not something a policy says about {section.Governs}. It takes {DefaultsKey}, {ForcedKey}, and {EditingKey}.");
            }
        }
    }

    /// <summary>Judges the defaults or the forced values of one section, which are a sparse document in the shape of the record it governs.</summary>
    private void JudgeStatement(SettingsPolicySection section, string statement, JsonNode? stated)
    {
        var where = $"{section.Name}:{statement}";

        if (stated is not JsonObject document)
        {
            this.refusals.Add(
                $"{where} is an object in the shape of {section.Governs}, stating only the properties the policy speaks for.");

            return;
        }

        var values = new List<(GovernableProperty Property, JsonNode Value)>();

        this.Walk(section, statement, document, prefix: string.Empty, values);

        if (section == SettingsPolicySection.MailAccounts)
        {
            this.mailAccountValues.UnionWith(
                values.Select(stated => $"{statement}:{stated.Property.Path}={stated.Value.ToJsonString()}"));
        }

        // Each stated value is bound on its own, as the one property it is stated for. The binder stops at the first
        // value it cannot convert, so a statement bound whole would report one fault of several and leave the rest to
        // be found a save at a time.
        foreach (var (property, value) in values)
        {
            if (this.Bind(section, where, property, value) is { } record)
            {
                this.refusals.AddRange(FindValueRefusals(where, property, record));
                this.refusals.AddRange(section.FindOwnRuleRefusals(record).Select(refusal => $"{where}: {refusal}"));
            }
        }
    }

    /// <summary>Holds each stated property against the record's own, and collects the values that are a policy's to state.</summary>
    /// <remarks>
    /// The catalog rather than the binder is what decides whether a path names anything, because the binder accepts a
    /// name it cannot write — a view computed from the settings binds without complaint and changes nothing.
    /// </remarks>
    private void Walk(
        SettingsPolicySection section,
        string statement,
        JsonObject document,
        string prefix,
        List<(GovernableProperty Property, JsonNode Value)> values)
    {
        var where = $"{section.Name}:{statement}";

        foreach (var (name, value) in this.MembersOf(document, prefix.Length == 0 ? where : $"{where} at {prefix.TrimEnd(':')}"))
        {
            if (!StrictBindingFailure.IsSettingPath(name))
            {
                this.refusals.Add($"{where} names {Repeatable(name)}, which names nothing a policy governs. Remove it.");

                continue;
            }

            // A joined key binds exactly as the nested one does, so accepting it would store one property under two
            // spellings, and whatever reads a policy afterwards would have to know both.
            if (name.Contains(':', StringComparison.Ordinal))
            {
                this.refusals.Add(
                    $"{where} names '{name}' as one key, and inside {statement} a property beneath a block is stated inside that block rather than joined to it by a colon. Nest it.");

                continue;
            }

            var written = $"{prefix}{name}";

            if (section == SettingsPolicySection.Users && Names(written, ClientPreferencesKey))
            {
                this.JudgeClientPreferences(statement, value);
            }
            else if (section.Properties.FindReasonOutside(written) is { } reason)
            {
                this.refusals.Add($"{where} names {written}, which is {reason}. Remove it.");
            }
            else if (section.Properties.Find(written) is not { } property)
            {
                this.refusals.Add(
                    $"{where} names {written}, which is not a property of {section.Governs}. Remove it, or correct the spelling of the property it was meant to be.");
            }
            else if (property.Shape == GovernablePropertyShape.Secret)
            {
                this.refusals.Add(
                    $"{where} states {property.Path}, and a policy states nothing about the value of a secret block: a secret reference is admissible for one user, and a policy speaks for every record in its scope. Remove it; {section.Name}:{EditingKey} may still say whether the person changes it.");
            }
            else if (property.PropertyClass == SettingsPolicyPropertyClass.Identity)
            {
                this.refusals.Add(
                    $"{where} states {property.Path}, which says who or which rather than how, so it takes no default and no forced value: one statement for many records could only be wrong. Remove it; {section.Name}:{EditingKey} may still say whether the person changes it.");
            }
            else if (property.Shape == GovernablePropertyShape.Block)
            {
                if (value is JsonObject block)
                {
                    this.Walk(section, statement, block, $"{property.Path}:", values);
                }
                else
                {
                    this.refusals.Add(
                        $"{where} gives {property.Path} one value, and {property.Path} is a block of settings. State the properties beneath it, each on its own.");
                }
            }
            else if (FindShapeRefusal(where, property, value) is { } misshapen)
            {
                this.refusals.Add(misshapen);
            }
            else
            {
                values.Add((property, value!));
            }
        }
    }

    /// <summary>Says why a stated value is not shaped as its property is, or nothing where it is.</summary>
    /// <remarks>
    /// Asked before the binder is, because the binder reads a shape it did not expect as something else rather than
    /// refusing it: an object under a value's name flattens into keys nothing binds, and an absent value leaves the
    /// property at its built-in default — a statement that would then read as made and govern nothing.
    /// </remarks>
    private static string? FindShapeRefusal(string where, GovernableProperty property, JsonNode? value)
    {
        if (value is null
            || (value is JsonValue scalar && scalar.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text)))
        {
            return $"{where} names {property.Path} and states no value for it. State one, or remove it.";
        }

        return (property.Shape, value) switch
        {
            (GovernablePropertyShape.List, not JsonArray) =>
                $"{where} gives {property.Path} one value, and {property.Path} is a list. State the whole list, which replaces the list beneath it rather than adding to it.",
            (GovernablePropertyShape.Value, not JsonValue) =>
                $"{where} gives {property.Path} more than one value, and {property.Path} takes one.",
            _ => null,
        };
    }

    /// <summary>Binds one stated value as the record it governs, strictly, or refuses it in a sentence about the policy.</summary>
    /// <returns>A record holding that one value and nothing else, or <see langword="null" /> where the value does not bind.</returns>
    private object? Bind(
        SettingsPolicySection section,
        string where,
        GovernableProperty property,
        JsonNode value)
    {
        var bindable = property.Path
            .Split(':')
            .Reverse()
            .Aggregate(value.DeepClone(), (JsonNode stated, string name) => new JsonObject { [name] = stated });

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(bindable.ToJsonString()), writable: false);

        // Built from the provider and released in a finally for the reasons the record's own binder gives: a
        // builder-built root would drop what it had constructed when the parse refuses, and one abandoned undisposed
        // leaves a parsed document behind.
        ConfigurationRoot? configuration = null;

        try
        {
            configuration = new([new JsonStreamConfigurationSource { Stream = stream }.Build(new ConfigurationBuilder())]);

            return configuration.Get(section.RecordType, binder => binder.ErrorOnUnknownConfiguration = true)
                ?? Activator.CreateInstance(section.RecordType);
        }
        catch (InvalidOperationException refusal)
        {
            this.refusals.Add(StrictBindingFailure.Read(refusal) switch
            {
                { UnknownProperties: { } names } =>
                    $"{where} names {names} inside a list, which is not a setting an entry of that list carries. Remove it, or correct the spelling of the setting it was meant to be.",
                { UnconvertiblePath: { } path } =>
                    $"The value {where} gives {path} is not of the type that setting takes. Correct it to the type the setting is declared as.",
                _ => DoesNotBind(where, property),
            });

            return null;
        }
        catch (Exception refusal) when (refusal is FormatException or JsonException)
        {
            this.refusals.Add(DoesNotBind(where, property));

            return null;
        }
        finally
        {
            configuration?.Dispose();
        }
    }

    /// <summary>Asks of one bound value what the binder does not: that an enumeration's member carries it, and that it passes the rules its property declares.</summary>
    private static IEnumerable<string> FindValueRefusals(string where, GovernableProperty property, object record)
    {
        var (owner, value) = property.ReadFrom(record);

        if (owner is null || value is null)
        {
            yield break;
        }

        // The binder converts a bare number onto an enumeration without asking whether any member carries it, so an
        // undefined one binds and would be read as whichever member the code compares against last.
        if (property.ValueType is { IsEnum: true } enumeration && !IsCarriedByAMember(enumeration, value))
        {
            yield return
                $"{where} gives {property.Path} a value that names none of {string.Join(", ", Enum.GetNames(enumeration))}.";
        }

        var declared = new ValidationContext(owner) { MemberName = property.Accessors[^1].Name };

        foreach (var rule in property.Accessors[^1].GetCustomAttributes<ValidationAttribute>())
        {
            if (rule.GetValidationResult(value, declared) is { ErrorMessage: { } message })
            {
                yield return $"{where} gives {property.Path} a value that setting refuses: {message}";
            }
        }

        // A list is stated whole, so each entry of one is a whole value and is asked the rules it declares for
        // itself — which is the one place a rule the record writes in code rather than as an attribute can be asked of
        // a statement, because an entry has no siblings the record states elsewhere.
        if (property.Shape == GovernablePropertyShape.List && value is IEnumerable entries)
        {
            foreach (var refusal in entries
                .OfType<IValidatableObject>()
                .SelectMany(entry => entry.Validate(new ValidationContext(entry)))
                .Select(result => result.ErrorMessage)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal))
            {
                // An entry's rule quotes the name the entry was given, which is text whoever wrote the policy chose.
                yield return refusal.Any(char.IsControl)
                    ? $"{where} gives {property.Path} an entry that list refuses."
                    : $"{where} gives {property.Path} an entry that list refuses: {refusal}";
            }
        }
    }

    private static string DoesNotBind(string where, GovernableProperty property) =>
        $"{where} gives {property.Path} a value that does not bind as that setting. Check it against what the setting takes in a record.";

    /// <summary>Reports whether an enumeration's value is one of its members, or for a set of flags a combination of them.</summary>
    private static bool IsCarriedByAMember(Type enumeration, object value)
    {
        if (!enumeration.IsDefined(typeof(FlagsAttribute), inherit: false))
        {
            return Enum.IsDefined(enumeration, value);
        }

        var members = Enum.GetValuesAsUnderlyingType(enumeration)
            .Cast<object>()
            .Aggregate(0UL, (known, member) => known | Convert.ToUInt64(member, CultureInfo.InvariantCulture));

        return (Convert.ToUInt64(value, CultureInfo.InvariantCulture) & ~members) == 0;
    }

    /// <summary>Judges what a statement about a user says of their client preferences, of which a policy forces one and governs no other.</summary>
    private void JudgeClientPreferences(string statement, JsonNode? stated)
    {
        var where = $"{SettingsPolicySection.Users.Name}:{statement}";
        var telemetry = $"{ClientPreferencesKey}:{nameof(ClientPreferences.TelemetryEnabled)}";

        if (stated is not JsonObject preferences)
        {
            this.refusals.Add(
                $"{where} gives {ClientPreferencesKey} one value, and the one thing a policy says about a client preference is a forced {telemetry}.");

            return;
        }

        foreach (var (name, value) in this.MembersOf(preferences, $"{where} at {ClientPreferencesKey}"))
        {
            if (!Names(name, nameof(ClientPreferences.TelemetryEnabled)))
            {
                this.refusals.Add(ClientPreferenceNames.FirstOrDefault(known => Names(name, known)) is { } preference
                    ? $"{where} states {ClientPreferencesKey}:{preference}, and a client preference is the person's own: a policy may force {telemetry} and says nothing about any other. Remove it."
                    : $"{where} names {ClientPreferencesKey}:{Repeatable(name)}, which is not a client preference. Remove it.");
            }
            else if (statement != ForcedKey)
            {
                this.refusals.Add(
                    $"{where} states {telemetry}, which takes a forced value and nothing else: a default would answer for a person a question they have not been asked. Remove it, or state it under {SettingsPolicySection.Users.Name}:{ForcedKey}.");
            }
            else if (value is not JsonValue forced || !forced.TryGetValue<bool>(out _))
            {
                this.refusals.Add($"{where} gives {telemetry} a value that is neither true nor false.");
            }
        }
    }

    /// <summary>Judges the editing restriction of one section: its mode, and the properties its list names.</summary>
    private void JudgeEditing(SettingsPolicySection section, JsonNode? stated)
    {
        var where = $"{section.Name}:{EditingKey}";

        if (stated is not JsonObject editing)
        {
            this.refusals.Add($"{where} is an object holding {EditingModeKey} and {EditingPropertiesKey}.");

            return;
        }

        SettingsPolicyEditingMode? mode = null;
        JsonNode? listed = null;
        var statesAList = false;
        var statesAMode = false;

        foreach (var (name, value) in this.MembersOf(editing, where))
        {
            if (Names(name, EditingModeKey))
            {
                mode = ReadMode(value);
                statesAMode = true;

                if (mode is null)
                {
                    this.refusals.Add(
                        $"{where}:{EditingModeKey} takes '{nameof(SettingsPolicyEditingMode.AllExcept)}' or '{nameof(SettingsPolicyEditingMode.NoneExcept)}'.");
                }
            }
            else if (Names(name, EditingPropertiesKey))
            {
                listed = value;
                statesAList = true;
            }
            else
            {
                this.refusals.Add(
                    $"{where} names {Repeatable(name)}, which is not part of an editing restriction. It takes {EditingModeKey} and {EditingPropertiesKey}.");
            }
        }

        if (!statesAList)
        {
            return;
        }

        if (!statesAMode)
        {
            this.refusals.Add(
                $"{where} lists {EditingPropertiesKey} and states no {EditingModeKey}, so the list says neither which properties are locked nor which are open. State {EditingModeKey} as '{nameof(SettingsPolicyEditingMode.AllExcept)}' or '{nameof(SettingsPolicyEditingMode.NoneExcept)}'.");
        }

        if (listed is not JsonArray paths
            || paths.Any(path => path is not JsonValue text || !text.TryGetValue<string>(out _)))
        {
            this.refusals.Add(
                $"{where}:{EditingPropertiesKey} is a list of property paths, each the record's own key names joined by colons.");

            return;
        }

        // A list under a mode that is missing or unreadable is still held to what every mode asks of a path, so a
        // mistyped one is not first reported once the mode has been corrected.
        var chosen = mode ?? SettingsPolicyEditingMode.AllExcept;

        this.refusals.AddRange(paths
            .Select(path => FindListedPathRefusal(section, chosen, path!.GetValue<string>()))
            .OfType<string>());
    }

    private static SettingsPolicyEditingMode? ReadMode(JsonNode? stated) =>
        stated is JsonValue value && value.TryGetValue<string>(out var written)
            ? Enum.GetValues<SettingsPolicyEditingMode>()
                .Where(mode => Names(written, mode.ToString()))
                .Select(mode => (SettingsPolicyEditingMode?)mode)
                .FirstOrDefault()
            : null;

    /// <summary>Says why a path an editing list names may not stand there, or nothing where it may.</summary>
    private static string? FindListedPathRefusal(
        SettingsPolicySection section,
        SettingsPolicyEditingMode mode,
        string path)
    {
        var where = $"{section.Name}:{EditingKey}:{EditingPropertiesKey}";

        if (!StrictBindingFailure.IsSettingPath(path))
        {
            return $"{where} lists {Repeatable(path)}, which is not a property path: the record's own key names joined by colons.";
        }

        if (section == SettingsPolicySection.Users
            && (Names(path, ClientPreferencesKey)
                || path.StartsWith($"{ClientPreferencesKey}:", StringComparison.OrdinalIgnoreCase)))
        {
            return $"{where} lists {path}, and no editing restriction reaches a client preference: each is the person's own, and the one a policy holds is held by forcing {ClientPreferencesKey}:{nameof(ClientPreferences.TelemetryEnabled)}. Remove it.";
        }

        if (section.Properties.FindReasonOutside(path) is { } reason)
        {
            return $"{where} lists {path}, which is {reason}. Remove it.";
        }

        if (section.Properties.Find(path) is not { } property)
        {
            return section.Properties.FindListReachedInto(path) is { } list
                ? $"{where} lists {path}, which reaches inside {list.Path}, and a list is one value: no path names an entry of one. List {list.Path} itself."
                : $"{where} lists {path}, which is not a property of {section.Governs}. Remove it, or correct the spelling of the property it was meant to be.";
        }

        return mode == SettingsPolicyEditingMode.NoneExcept && section.Properties.HoldsAdministratorOnly(property)
            ? $"{where} lists {property.Path} as the person's to change, and {(property.PropertyClass == SettingsPolicyPropertyClass.AdministratorOnly ? "it is written" : "it holds what is written")} by an administrator alone: no editing mode makes that theirs. Remove it from the list."
            : null;
    }

    /// <summary>Reads an object's members, refusing a name that two of them spell in different casing.</summary>
    /// <remarks>
    /// Names are compared without regard to case everywhere a policy is read, as a path within a record is, so two
    /// spellings of one name are one statement made twice — and which of the two a reader took would depend on the
    /// order the document happened to hold them in.
    /// </remarks>
    private KeyValuePair<string, JsonNode?>[] MembersOf(JsonObject stated, string where)
    {
        KeyValuePair<string, JsonNode?>[] members = [.. stated];

        this.refusals.AddRange(members
            .GroupBy(member => member.Key, StringComparer.OrdinalIgnoreCase)
            .Where(spellings => spellings.Count() > 1)
            .Select(spellings => $"{where} states {Repeatable(spellings.Key)} more than once, in different casing. State it once."));

        return [.. members.DistinctBy(member => member.Key, StringComparer.OrdinalIgnoreCase)];
    }

    private static bool Names(string written, string name) =>
        string.Equals(written, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets a key somebody wrote back where it is safe to repeat, and a description of it where it is not.</summary>
    private static string Repeatable(string written) =>
        StrictBindingFailure.IsSettingPath(written) ? $"'{written}'" : "a name that is not a setting's";
}
