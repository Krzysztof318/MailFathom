// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>Produces the user record a targeted change would leave, without judging what it produced.</summary>
/// <remarks>
/// <para>
/// Adding and removing one mailbox are the two acts an operator performs far more often than replacing a whole record,
/// and expressing either as a document the caller composes would make a mistyped brace the difference between adding a
/// mailbox and replacing every one of them. So the caller states the act, this states the candidate, and the binder
/// beside it is what decides whether the candidate is a record at all — exactly as it decides for a whole document
/// somebody edited by hand. The three folder acts beside them are the same argument one level in, and they are the
/// ones a person rather than an operator reaches: a client that let somebody make a folder by editing their whole
/// record would be asking them to type a document to name a folder.
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
/// that is what an operator holds and what the naming rules make unique within a user; everything else about an entry
/// travels unread.
/// </para>
/// </remarks>
internal static class UserRecordComposition
{
    /// <summary>The property a user's record holds their mail accounts under.</summary>
    private const string MailAccountsProperty = nameof(UserAccountOptions.MailAccounts);

    /// <summary>The property one mail-account declaration is identified by within its user.</summary>
    private const string AccountIdProperty = "AccountId";

    /// <summary>The property one mail account holds its folders under.</summary>
    private const string FoldersProperty = "Folders";

    /// <summary>The property one folder declaration is identified by within its account.</summary>
    private const string AliasProperty = "Alias";

    /// <summary>The one character a folder alias nests on, which is what a level of one is counted by.</summary>
    private const char AliasSeparator = '/';

    /// <summary>How many levels a folder alias may nest, which is the ceiling the client's own tree draws to.</summary>
    /// <remarks>
    /// Stated here as well as in the client because it is a rule about what may be written rather than about what may
    /// be drawn: a caller reaching the folder routes without the dialog would otherwise declare a mailbox nested past
    /// the depth any screen offers, and every reader of that record — the tree above all — would then be recursing
    /// through a shape nothing bounded.
    /// </remarks>
    private const int DeepestAlias = 3;

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

        var record = ObjectOf(json, "The user record is not a JSON object, so there is nothing for a mail account to be added to.");

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

        var record = ObjectOf(json, "The user record is not a JSON object, so there is no mail account in it to remove.");
        var declarations = DeclarationsIn(record);

        var kept = declarations
            .Where(declaration => !NamesAccount(declaration, accountId))
            .ToArray();

        return kept.Length == declarations.Count ? null : Rewritten(record, kept);
    }

    /// <summary>Produces the record one more folder in one mail account would leave.</summary>
    /// <param name="json">The record as it stands.</param>
    /// <param name="accountId">The identifier the account the folder belongs to is named by.</param>
    /// <param name="folderJson">The folder to declare, as the JSON object a file would have written.</param>
    /// <returns>The candidate record, or <see langword="null" /> when the record declares no account under that identifier.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json" /> or <paramref name="folderJson" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="FormatException">Thrown when the record is not a JSON object, when the folder is not one, or when the folder's alias nests past the depth a folder may be declared at.</exception>
    /// <exception cref="JsonException">Thrown when either is not JSON at all.</exception>
    /// <remarks>Appended rather than merged over an entry carrying the same alias, for the reason a mail account is: an alias somebody already declared is a collision the naming rules refuse rather than settings to overwrite unread.</remarks>
    public static string? WithFolderAdded(string json, string accountId, string folderJson)
    {
        ArgumentNullException.ThrowIfNull(folderJson);

        var folder = ObjectOf(
            folderJson,
            "A folder declaration is a JSON object of that folder's settings, and this is not one.");

        RefuseAnAliasNestedTooDeep(folder);

        return WithFoldersOf(json, accountId, folders => [.. folders, folder]);
    }

    /// <summary>Produces the record one folder stated afresh would leave, in place of the one carrying an alias.</summary>
    /// <param name="json">The record as it stands.</param>
    /// <param name="accountId">The identifier the account the folder belongs to is named by.</param>
    /// <param name="alias">The alias the folder to replace is declared under.</param>
    /// <param name="folderJson">The folder as it is to stand, as the JSON object a file would have written.</param>
    /// <returns>The candidate record, or <see langword="null" /> when no such account declares a folder under that alias.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json" /> or <paramref name="folderJson" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId" /> or <paramref name="alias" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="FormatException">Thrown when the record is not a JSON object, when the folder is not one, or when the folder's alias nests past the depth a folder may be declared at.</exception>
    /// <exception cref="JsonException">Thrown when either is not JSON at all.</exception>
    /// <remarks>
    /// The entry is replaced where it stands rather than removed and appended, so a folder somebody renames keeps its
    /// position among its siblings — which is the order the account's folders bind in and therefore the order a later
    /// keyed change addresses them by.
    /// </remarks>
    public static string? WithFolderReplaced(string json, string accountId, string alias, string folderJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentNullException.ThrowIfNull(folderJson);

        var folder = ObjectOf(
            folderJson,
            "A folder declaration is a JSON object of that folder's settings, and this is not one.");

        RefuseAnAliasNestedTooDeep(folder);

        return WithFoldersOf(json, accountId, folders =>
            folders.Any(declared => NamesFolder(declared, alias))
                ? [.. folders.Select(declared => NamesFolder(declared, alias) ? folder : declared)]
                : null);
    }

    /// <summary>Produces the record one fewer folder in one mail account would leave.</summary>
    /// <param name="json">The record as it stands.</param>
    /// <param name="accountId">The identifier the account the folder belongs to is named by.</param>
    /// <param name="alias">The alias the folder to withdraw is declared under.</param>
    /// <returns>The candidate record, or <see langword="null" /> when no such account declares a folder under that alias.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId" /> or <paramref name="alias" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="FormatException">Thrown when the record is not a JSON object.</exception>
    /// <exception cref="JsonException">Thrown when the record is not JSON at all.</exception>
    /// <remarks>What it withdraws is the one entry the alias names and nothing nested under it: an alias is text to this type, and what a slash in one means to the tree a screen draws is decided by the caller that composes the removal rather than rediscovered here.</remarks>
    public static string? WithFolderRemoved(string json, string accountId, string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        return WithFoldersOf(json, accountId, folders =>
        {
            var kept = folders.Where(declared => !NamesFolder(declared, alias)).ToArray();

            return kept.Length == folders.Count ? null : kept;
        });
    }

    /// <summary>Reads the aliases one account in one record declares folders under.</summary>
    /// <param name="json">The record.</param>
    /// <param name="accountId">The identifier the account is named by.</param>
    /// <returns>The aliases, in the order the account declares them, skipping an entry that states none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="FormatException">Thrown when the record is not a JSON object.</exception>
    /// <exception cref="JsonException">Thrown when the record is not JSON at all.</exception>
    public static IReadOnlyList<string> FolderAliasesIn(string json, string accountId)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        var record = ObjectOf(json, "The user record is not a JSON object, so it declares no mail accounts.");

        return DeclarationsIn(record).FirstOrDefault(declaration => NamesAccount(declaration, accountId)) is JsonObject account
            ? [.. FoldersIn(account).Select(folder => ValueOf(folder, AliasProperty)).OfType<string>()]
            : [];
    }

    /// <summary>Rewrites one account's folders with whatever the change makes of them, leaving every other account as it was.</summary>
    /// <remarks>
    /// The one place the three acts above meet, so an entry they each have to find, keep in order, and write back in
    /// the keyed shape is found, kept, and written once. A change answering nothing is a change that matched no
    /// folder, and it travels out as the absence the caller reports rather than as a record identical to the one it
    /// was handed.
    /// </remarks>
    private static string? WithFoldersOf(
        string json,
        string accountId,
        Func<IReadOnlyList<JsonNode>, JsonNode[]?> change)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        var record = ObjectOf(json, "The user record is not a JSON object, so there is no folder in it to change.");
        var declarations = DeclarationsIn(record);
        var standing = declarations.OfType<JsonObject>().FirstOrDefault(declaration => NamesAccount(declaration, accountId));

        if (standing is null || change(FoldersIn(standing)) is not { } folders)
        {
            return null;
        }

        var replaced = standing.DeepClone().AsObject();

        replaced.Remove(ExistingNameOf(replaced, FoldersProperty));

        if (folders.Length > 0)
        {
            replaced[FoldersProperty] = KeyedByPosition(folders);
        }

        return Rewritten(record, [.. declarations.Select(declaration => ReferenceEquals(declaration, standing) ? replaced : declaration)]);
    }

    /// <summary>Reads the folders one account declaration holds, in the order the configuration layer binds them.</summary>
    private static IReadOnlyList<JsonNode> FoldersIn(JsonObject account) =>
        PropertyOf(account, FoldersProperty) switch
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

    /// <summary>Reports whether one folder declaration is the one an alias names, compared as an alias is compared.</summary>
    /// <remarks>Case-insensitively, because <c>MailFolderAlias</c> upper-cases what it is given so that one folder is one value in a database whose collation MailFathom does not control.</remarks>
    /// <summary>Refuses a declaration whose alias nests past the depth a folder may be declared at.</summary>
    /// <remarks>
    /// Raised rather than answered, so the sentence travels out through the same refusal a malformed declaration takes
    /// and the caller is told which alias and how deep it went instead of being handed a record that quietly kept it.
    /// A declaration stating no alias is passed over here, because the binder beside this refuses one with a sentence
    /// naming that rule and a second refusal would say the same thing twice in the caller's own words.
    /// </remarks>
    private static void RefuseAnAliasNestedTooDeep(JsonObject folder)
    {
        if (ValueOf(folder, AliasProperty) is not { } alias)
        {
            return;
        }

        var levels = alias.Split(AliasSeparator).Count(segment => !string.IsNullOrWhiteSpace(segment));

        if (levels > DeepestAlias)
        {
            throw new FormatException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A folder alias nests at most {DeepestAlias} levels, and '{alias}' carries {levels}."));
        }
    }

    private static bool NamesFolder(JsonNode declaration, string alias) =>
        ValueOf(declaration, AliasProperty) is { } declared
        && declared.Trim().Equals(alias.Trim(), StringComparison.OrdinalIgnoreCase);

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

        var record = ObjectOf(json, "The user record is not a JSON object, so it declares no mail accounts.");

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
    /// The record is cloned rather than mutated so a refused candidate leaves the caller holding what it read. A user
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
            candidate[MailAccountsProperty] = KeyedByPosition(declarations);
        }

        return candidate.ToJsonString();
    }

    /// <summary>Writes a collection of declarations as the object keyed by position that a configuration key flattens from.</summary>
    /// <remarks>
    /// Every node is cloned on the way in, because a node already belongs to the graph it was read out of and adding
    /// it to a second one would take it out of the first — which is the record a refused candidate leaves the caller
    /// holding.
    /// </remarks>
    private static JsonObject KeyedByPosition(IReadOnlyList<JsonNode> declarations)
    {
        var keyed = new JsonObject();

        foreach (var (index, declaration) in declarations.Index())
        {
            keyed[index.ToString(CultureInfo.InvariantCulture)] = declaration.DeepClone();
        }

        return keyed;
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
