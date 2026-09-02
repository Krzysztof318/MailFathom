// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MailFathom.Host.Configuration.OwnerSettings.Administration;

/// <summary>Produces the owner record a targeted change would leave, without judging what it produced.</summary>
/// <remarks>
/// <para>
/// Adding and removing one mailbox are the two acts an operator performs far more often than replacing a whole record,
/// and expressing either as a document the caller composes would make a mistyped brace the difference between adding a
/// mailbox and replacing every one of them. So the caller states the act, this states the candidate, and the binder
/// beside it is what decides whether the candidate is a record at all — exactly as it decides for a whole document
/// somebody edited by hand.
/// </para>
/// <para>
/// The collection is written back as an object keyed by position rather than as a JSON array, which is the shape
/// <c>docs/operations/configuration-sources.md</c> tells an operator to write and the shape a keyed change to the
/// deployment's own document produces. The two are the same configuration keys; what the object buys is that a later
/// change addressing <c>MailAccounts:1</c> reaches the entry that was at position one rather than whichever element a
/// renumbering left there.
/// </para>
/// <para>
/// Nothing here reads a value for meaning. Which account a removal names is matched on the declared identifier because
/// that is what an operator holds and what the naming rules make unique within an owner; everything else about an entry
/// travels unread.
/// </para>
/// </remarks>
internal static class OwnerRecordComposition
{
    /// <summary>The property an owner's record holds their mail accounts under.</summary>
    private const string MailAccountsProperty = nameof(OwnerAccountOptions.MailAccounts);

    /// <summary>The property one mail-account declaration is identified by within its owner.</summary>
    private const string AccountIdProperty = "AccountId";

    /// <summary>Produces the record one more mail account would leave.</summary>
    /// <param name="json">The record as it stands.</param>
    /// <param name="accountJson">The declaration to add, as the JSON object a file would have written.</param>
    /// <returns>The candidate record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the record as it stands is not a JSON object, or when the declaration is not one.</exception>
    /// <exception cref="JsonException">Thrown when either is not JSON at all.</exception>
    /// <remarks>Appended rather than merged over an existing entry of the same identifier, so that adding a mailbox somebody already declared is refused by the naming rules as the collision it is instead of quietly replacing their settings.</remarks>
    public static string WithMailAccountAdded(string json, string accountJson)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(accountJson);

        var record = ObjectOf(json, "The owner record is not a JSON object, so there is nothing for a mail account to be added to.");

        var account = ObjectOf(
            accountJson,
            "A mail-account declaration is a JSON object of that account's settings, and this is not one.");

        return Rewritten(record, [.. DeclarationsIn(record), account]);
    }

    /// <summary>Produces the record one fewer mail account would leave.</summary>
    /// <param name="json">The record as it stands.</param>
    /// <param name="accountId">The identifier the declaration to remove is named by.</param>
    /// <returns>The candidate record, or <see langword="null" /> when the record declares no account under that identifier.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="FormatException">Thrown when the record as it stands is not a JSON object.</exception>
    /// <exception cref="JsonException">Thrown when the record is not JSON at all.</exception>
    /// <remarks>
    /// Absence answers with nothing rather than with the record unchanged, because the two are different things to
    /// report: a removal that matched nothing is an identifier the caller got wrong, and telling them the record is
    /// fine would leave them believing a mailbox had stopped being synchronized.
    /// </remarks>
    public static string? WithMailAccountRemoved(string json, string accountId)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        var record = ObjectOf(json, "The owner record is not a JSON object, so there is no mail account in it to remove.");
        var declarations = DeclarationsIn(record);

        var kept = declarations
            .Where(declaration => !NamesAccount(declaration, accountId))
            .ToArray();

        return kept.Length == declarations.Count ? null : Rewritten(record, kept);
    }

    /// <summary>Reads the identifiers one record declares mail accounts under.</summary>
    /// <param name="json">The record.</param>
    /// <returns>The identifiers, in the order the record declares them, skipping an entry that states none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the record is not a JSON object.</exception>
    /// <exception cref="JsonException">Thrown when the record is not JSON at all.</exception>
    /// <remarks>An entry stating no identifier is passed over rather than reported, because the binder beside this refuses such a record with a sentence naming the rule; a listing is not where that is discovered.</remarks>
    public static IReadOnlyList<string> MailAccountIdentifiersIn(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var record = ObjectOf(json, "The owner record is not a JSON object, so it declares no mail accounts.");

        return
        [
            .. DeclarationsIn(record)
                .Select(declaration => ValueOf(declaration, AccountIdProperty))
                .OfType<string>(),
        ];
    }

    /// <summary>Reads the declarations a record holds, whichever of the two shapes the collection was written in.</summary>
    /// <remarks>
    /// Both shapes flatten to the same configuration keys, so both are records this deployment reads, and a document
    /// somebody edited by hand routinely carries the array. They are ordered the way the configuration layer orders
    /// them rather than the way the document lists them, because that is the order the record binds in and therefore
    /// the order the positions actually mean.
    /// </remarks>
    private static IReadOnlyList<JsonNode> DeclarationsIn(JsonObject record) =>
        PropertyOf(record, MailAccountsProperty) switch
        {
            JsonArray declared => [.. declared.OfType<JsonNode>()],
            JsonObject keyed =>
            [
                .. keyed
                    .OrderBy(entry => entry.Key, ConfigurationKeyComparer.Instance)
                    .Select(entry => entry.Value)
                    .OfType<JsonNode>(),
            ],
            _ => [],
        };

    /// <summary>Writes the collection back into the record, keyed by position.</summary>
    /// <remarks>
    /// The record is cloned rather than mutated so a refused candidate leaves the caller holding what it read. An owner
    /// declaring nothing carries no collection at all rather than an empty one, which is what a removal of the last
    /// mailbox has to leave: an empty object contributes no configuration key either way, and a record describing a
    /// collection nobody declares is one the next reader takes for an unfinished edit.
    /// </remarks>
    private static string Rewritten(JsonObject record, JsonNode[] declarations)
    {
        // Defensive rather than observable: every caller hands this a graph parsed from a string it was given, so
        // nothing outside could see the record mutated. It is what keeps that true if a caller is ever given a node.
        var candidate = record.DeepClone().AsObject();

        candidate.Remove(ExistingNameOf(candidate, MailAccountsProperty));

        if (declarations.Length > 0)
        {
            var keyed = new JsonObject();

            foreach (var (index, declaration) in declarations.Index())
            {
                keyed[index.ToString(CultureInfo.InvariantCulture)] = declaration.DeepClone();
            }

            candidate[MailAccountsProperty] = keyed;
        }

        return candidate.ToJsonString();
    }

    /// <summary>Reports whether one declaration is the account an identifier names, compared as configuration compares a key.</summary>
    private static bool NamesAccount(JsonNode declaration, string accountId) =>
        ValueOf(declaration, AccountIdProperty) is { } declared
        && declared.Trim().Equals(accountId.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads one property of a declaration as text, or nothing where it holds no value.</summary>
    private static string? ValueOf(JsonNode declaration, string property) =>
        declaration is JsonObject entry && PropertyOf(entry, property) is JsonValue value
            ? value.ToString()
            : null;

    /// <summary>Reads a property, matching the name the way every configuration provider in the pipeline matches one.</summary>
    private static JsonNode? PropertyOf(JsonObject parent, string property) =>
        parent.TryGetPropertyValue(ExistingNameOf(parent, property), out var value) ? value : null;

    /// <summary>Finds how the record already spells a property, so one setting never acquires a second spelling.</summary>
    private static string ExistingNameOf(JsonObject parent, string property) => parent
        .Select(entry => entry.Key)
        .FirstOrDefault(key => key.Equals(property, StringComparison.OrdinalIgnoreCase))
        ?? property;

    /// <summary>Parses one document, refusing anything whose root is not the object a record is.</summary>
    private static JsonObject ObjectOf(string json, string refusal) =>
        JsonNode.Parse(json) as JsonObject ?? throw new FormatException(refusal);
}
