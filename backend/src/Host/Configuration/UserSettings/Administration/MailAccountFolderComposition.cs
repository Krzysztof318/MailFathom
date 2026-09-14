// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>Produces the mail-account settings one folder change would leave, without judging what it produced.</summary>
/// <remarks>
/// <para>
/// A client that let somebody make a folder by editing the whole account would be asking them to type a document to name
/// a folder. So the caller states the act, this states the candidate, and the binder is what decides whether the
/// candidate is an account at all — exactly as it decides for a document somebody edited by hand.
/// </para>
/// <para>
/// The folders are written back as an object keyed by position rather than as a JSON array, which is the shape a keyed
/// change produces: a later change addressing <c>Folders:1</c> reaches the entry that was at position one rather than
/// whichever element a renumbering left there. Nothing here reads a value for meaning beyond the alias a change names.
/// </para>
/// </remarks>
internal static class MailAccountFolderComposition
{
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
    /// the depth any screen offers.
    /// </remarks>
    private const int DeepestAlias = 3;

    /// <summary>Produces the settings one more folder would leave.</summary>
    /// <param name="accountJson">The account's settings as they stand.</param>
    /// <param name="folderJson">The folder to declare, as the JSON object a file would have written.</param>
    /// <returns>The candidate settings.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when either is not a JSON object, or when the folder's alias nests past the depth a folder may be declared at.</exception>
    /// <exception cref="JsonException">Thrown when either is not JSON at all.</exception>
    /// <remarks>Appended rather than merged over an entry carrying the same alias: an alias somebody already declared is a collision the naming rules refuse rather than settings to overwrite unread.</remarks>
    public static string WithFolderAdded(string accountJson, string folderJson)
    {
        var folder = FolderOf(folderJson);

        return WithFoldersOf(accountJson, folders => [.. folders, folder])!;
    }

    /// <summary>Produces the settings one folder stated afresh would leave, in place of the one carrying an alias.</summary>
    /// <param name="accountJson">The account's settings as they stand.</param>
    /// <param name="alias">The alias the folder to replace is declared under.</param>
    /// <param name="folderJson">The folder as it is to stand, as the JSON object a file would have written.</param>
    /// <returns>The candidate settings, or <see langword="null" /> when the account declares no folder under that alias.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accountJson" /> or <paramref name="folderJson" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="alias" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="FormatException">Thrown when either is not a JSON object, or when the folder's alias nests too deep.</exception>
    /// <exception cref="JsonException">Thrown when either is not JSON at all.</exception>
    /// <remarks>The entry is replaced where it stands rather than removed and appended, so a renamed folder keeps its position among its siblings.</remarks>
    public static string? WithFolderReplaced(string accountJson, string alias, string folderJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        var folder = FolderOf(folderJson);

        return WithFoldersOf(accountJson, folders =>
            folders.Any(declared => NamesFolder(declared, alias))
                ? [.. folders.Select(declared => NamesFolder(declared, alias) ? folder : declared)]
                : null);
    }

    /// <summary>Produces the settings one fewer folder would leave.</summary>
    /// <param name="accountJson">The account's settings as they stand.</param>
    /// <param name="alias">The alias the folder to withdraw is declared under.</param>
    /// <returns>The candidate settings, or <see langword="null" /> when the account declares no folder under that alias.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accountJson" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="alias" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="FormatException">Thrown when the settings are not a JSON object.</exception>
    /// <exception cref="JsonException">Thrown when the settings are not JSON at all.</exception>
    /// <remarks>What it withdraws is the one entry the alias names and nothing nested under it: what a slash in an alias means to the tree a screen draws is decided by the caller that composes the removal.</remarks>
    public static string? WithFolderRemoved(string accountJson, string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        return WithFoldersOf(accountJson, folders =>
        {
            var kept = folders.Where(declared => !NamesFolder(declared, alias)).ToArray();

            return kept.Length == folders.Count ? null : kept;
        });
    }

    /// <summary>Rewrites the account's folders with whatever the change makes of them.</summary>
    /// <remarks>A change answering nothing is a change that matched no folder, and it travels out as the absence the caller reports rather than as settings identical to the ones it was handed.</remarks>
    private static string? WithFoldersOf(string accountJson, Func<IReadOnlyList<JsonNode>, JsonNode[]?> change)
    {
        ArgumentNullException.ThrowIfNull(accountJson);

        var account = ObjectOf(accountJson, "The mail account's settings are not a JSON object, so there is no folder in them to change.");

        if (change(FoldersIn(account)) is not { } folders)
        {
            return null;
        }

        account.Remove(ExistingNameOf(account, FoldersProperty));

        if (folders.Length > 0)
        {
            account[FoldersProperty] = KeyedByPosition(folders);
        }

        return account.ToJsonString();
    }

    private static JsonObject FolderOf(string folderJson)
    {
        ArgumentNullException.ThrowIfNull(folderJson);

        var folder = ObjectOf(folderJson, "A folder declaration is a JSON object of that folder's settings, and this is not one.");

        RefuseAnAliasNestedTooDeep(folder);

        return folder;
    }

    /// <summary>Reads the folders one account holds, in the order the configuration layer binds them.</summary>
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

    /// <summary>Refuses a declaration whose alias nests past the depth a folder may be declared at.</summary>
    /// <remarks>A declaration stating no alias is passed over, because the binder refuses one with a sentence naming that rule and a second refusal would say the same thing twice.</remarks>
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

    /// <summary>Reports whether one folder declaration is the one an alias names.</summary>
    /// <remarks>Case-insensitively, because <c>MailFolderAlias</c> upper-cases what it is given.</remarks>
    private static bool NamesFolder(JsonNode declaration, string alias) =>
        ValueOf(declaration, AliasProperty) is { } declared
        && declared.Trim().Equals(alias.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes declarations as the object keyed by position that a configuration key flattens from.</summary>
    /// <remarks>Every node is cloned on the way in, because a node already belongs to the graph it was read out of.</remarks>
    private static JsonObject KeyedByPosition(IReadOnlyList<JsonNode> declarations)
    {
        var keyed = new JsonObject();

        foreach (var (index, declaration) in declarations.Index())
        {
            keyed[index.ToString(CultureInfo.InvariantCulture)] = declaration.DeepClone();
        }

        return keyed;
    }

    private static string? ValueOf(JsonNode declaration, string property) =>
        declaration is JsonObject entry && PropertyOf(entry, property) is JsonValue value
            ? value.ToString()
            : null;

    /// <summary>Reads a property, matching the name the way every configuration provider in the pipeline matches one.</summary>
    private static JsonNode? PropertyOf(JsonObject parent, string property) =>
        parent.TryGetPropertyValue(ExistingNameOf(parent, property), out var value) ? value : null;

    /// <summary>Finds how the settings already spell a property, so one setting never acquires a second spelling.</summary>
    private static string ExistingNameOf(JsonObject parent, string property) => parent
        .Select(entry => entry.Key)
        .FirstOrDefault(key => key.Equals(property, StringComparison.OrdinalIgnoreCase))
        ?? property;

    private static JsonObject ObjectOf(string json, string refusal) =>
        JsonNode.Parse(json) as JsonObject ?? throw new FormatException(refusal);
}
