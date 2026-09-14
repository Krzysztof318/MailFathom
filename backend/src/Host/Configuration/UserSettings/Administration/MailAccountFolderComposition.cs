// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>Produces the mail-account settings one folder change would leave, under the rules the client surface fixes.</summary>
/// <remarks>
/// <para>
/// A client that let somebody make a folder by editing the whole account would be asking them to type a document to name
/// a folder. So the caller states the act, this states the candidate, and the binder is what decides whether the
/// candidate is an account at all — exactly as it decides for a document somebody edited by hand.
/// </para>
/// <para>
/// What this does judge is the part of a declaration that is not the person's to state. A folder declared here is
/// always synchronized, and always created where it names a path its server advertises no folder at; its role is
/// stated when it is declared and never afterwards, and a folder playing a role is named after the role and stays.
/// Everything else — the remote path, the embeddings switch, the tool visibility — is theirs on every folder, special
/// ones included. None of it binds an administrator, who states a whole account rather than an act against one.
/// </para>
/// <para>
/// The folders are written back as an object keyed by position rather than as a JSON array, which is the shape a keyed
/// change produces: a later change addressing <c>Folders:1</c> reaches the entry that was at position one rather than
/// whichever element a renumbering left there. Nothing here reads a value for meaning beyond the alias a change names,
/// the role it plays, and the two switches above.
/// </para>
/// </remarks>
internal static class MailAccountFolderComposition
{
    /// <summary>The property one mail account holds its folders under.</summary>
    private const string FoldersProperty = "Folders";

    /// <summary>The property one folder declaration is identified by within its account.</summary>
    private const string AliasProperty = "Alias";

    /// <summary>The property naming where on the server a folder is, which is also where it would be created.</summary>
    private const string RemotePathProperty = "RemotePath";

    /// <summary>The property naming the role a folder plays for its account.</summary>
    private const string SpecialUseProperty = "SpecialUse";

    /// <summary>The property saying whether a folder's mail is mirrored locally.</summary>
    private const string SynchronizeProperty = "Synchronize";

    /// <summary>The property saying whether the folder is created where the server advertises none at its path.</summary>
    private const string CreateIfMissingProperty = "CreateIfMissing";

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
    /// <returns>The candidate settings, or the rule the declaration broke.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when either is not a JSON object, or when the folder's alias nests past the depth a folder may be declared at.</exception>
    /// <exception cref="JsonException">Thrown when either is not JSON at all.</exception>
    /// <remarks>Appended rather than merged over an entry carrying the same alias: an alias somebody already declared is a collision the naming rules refuse rather than settings to overwrite unread.</remarks>
    public static MailAccountFolderChange WithFolderAdded(string accountJson, string folderJson)
    {
        ArgumentNullException.ThrowIfNull(accountJson);

        var declared = Declared(FolderOf(folderJson));

        if (declared.Refusal is { } refusal)
        {
            return MailAccountFolderChange.Refused(refusal);
        }

        var account = AccountOf(accountJson);

        return MailAccountFolderChange.Composed(WithFolders(account, [.. FoldersIn(account), declared.Folder]));
    }

    /// <summary>Produces the settings one folder stated afresh would leave, in place of the one carrying an alias.</summary>
    /// <param name="accountJson">The account's settings as they stand.</param>
    /// <param name="alias">The alias the folder to replace is declared under.</param>
    /// <param name="folderJson">The folder as it is to stand, as the JSON object a file would have written.</param>
    /// <returns>The candidate settings, the rule the declaration broke, or neither when the account declares no folder under that alias.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accountJson" /> or <paramref name="folderJson" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="alias" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="FormatException">Thrown when either is not a JSON object, or when the folder's alias nests too deep.</exception>
    /// <exception cref="JsonException">Thrown when either is not JSON at all.</exception>
    /// <remarks>The entry is replaced where it stands rather than removed and appended, so a renamed folder keeps its position among its siblings.</remarks>
    public static MailAccountFolderChange WithFolderReplaced(string accountJson, string alias, string folderJson)
    {
        ArgumentNullException.ThrowIfNull(accountJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        var folder = FolderOf(folderJson);
        var account = AccountOf(accountJson);
        var folders = FoldersIn(account);

        if (folders.FirstOrDefault(declared => NamesFolder(declared, alias)) is not { } standing)
        {
            return MailAccountFolderChange.NoSuchFolder;
        }

        if (RefusedRoleChange(RoleOf(standing), folder, alias) is { } changed)
        {
            return MailAccountFolderChange.Refused(changed);
        }

        var declared = Declared(folder);

        return declared.Refusal is { } refusal
            ? MailAccountFolderChange.Refused(refusal)
            : MailAccountFolderChange.Composed(WithFolders(
                account,
                [.. folders.Select(entry => NamesFolder(entry, alias) ? declared.Folder : entry)]));
    }

    /// <summary>Produces the settings one fewer folder would leave.</summary>
    /// <param name="accountJson">The account's settings as they stand.</param>
    /// <param name="alias">The alias the folder to withdraw is declared under.</param>
    /// <returns>The candidate settings, the rule the withdrawal broke, or neither when the account declares no folder under that alias.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accountJson" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="alias" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="FormatException">Thrown when the settings are not a JSON object.</exception>
    /// <exception cref="JsonException">Thrown when the settings are not JSON at all.</exception>
    /// <remarks>What it withdraws is the one entry the alias names and nothing nested under it: what a slash in an alias means to the tree a screen draws is decided by the caller that composes the removal.</remarks>
    public static MailAccountFolderChange WithFolderRemoved(string accountJson, string alias)
    {
        ArgumentNullException.ThrowIfNull(accountJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        var account = AccountOf(accountJson);
        var folders = FoldersIn(account);

        if (folders.FirstOrDefault(declared => NamesFolder(declared, alias)) is not { } standing)
        {
            return MailAccountFolderChange.NoSuchFolder;
        }

        return RoleOf(standing) is { } role
            ? MailAccountFolderChange.Refused(
                $"Folder alias '{alias.Trim()}' plays the '{role}' role, and the folder playing a role is the one this account files by, so it is not withdrawn here.")
            : MailAccountFolderChange.Composed(WithFolders(account, [.. folders.Where(entry => !NamesFolder(entry, alias))]));
    }

    /// <summary>Holds every folder a whole mail account declares to the rules a folder route applies.</summary>
    /// <param name="accountJson">The account as the client declared it.</param>
    /// <returns>The settings the declaration would leave, or the rule one of its folders broke.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accountJson" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the settings are not a JSON object, or when a folder's alias nests too deep.</exception>
    /// <exception cref="JsonException">Thrown when the settings are not JSON at all.</exception>
    /// <remarks>
    /// A declared account carries its folders, so it is a folder route by another name and would otherwise be the way
    /// around the three. Every folder in it is new, which is why each is judged as a declaration and none as a change
    /// to one.
    /// </remarks>
    public static MailAccountFolderChange WithFoldersDeclared(string accountJson)
    {
        ArgumentNullException.ThrowIfNull(accountJson);

        var account = AccountOf(accountJson);
        List<JsonNode> declared = [];

        foreach (var entry in FoldersIn(account))
        {
            var folder = Declared(FolderOf(entry));

            if (folder.Refusal is { } refusal)
            {
                return MailAccountFolderChange.Refused(refusal);
            }

            declared.Add(folder.Folder);
        }

        return MailAccountFolderChange.Composed(WithFolders(account, [.. declared]));
    }

    /// <summary>Reads a folder declaration the client stated, refusing what this surface fixes and writing what it sets.</summary>
    /// <remarks>
    /// A declaration stating no alias is passed over, because the binder refuses one with a sentence naming that rule
    /// and every sentence here would name a folder by an alias it does not have.
    /// </remarks>
    private static (JsonObject Folder, string? Refusal) Declared(JsonObject folder)
    {
        if (ValueOf(folder, AliasProperty)?.Trim() is not { Length: > 0 } alias)
        {
            return (folder, null);
        }

        if (RefusedSwitch(folder, SynchronizeProperty, alias, "is always synchronized") is { } synchronize)
        {
            return (folder, synchronize);
        }

        if (MailFolderMappingOptions.TryParseSpecialUse(ValueOf(folder, SpecialUseProperty), out var role)
            && !alias.Equals(role.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return (
                folder,
                $"Folder alias '{alias}' plays the '{role}' role, and a folder playing a role carries the role's own name. Declare it as '{role}'.");
        }

        // A folder found by the role it plays is never created: it is whichever folder advertises the attribute, and a
        // folder that does not exist advertises nothing — which is why the binder refuses that pairing outright. So the
        // second switch is set where there is a path to create the folder at, and is nobody's to state where there is
        // not one.
        var createdAtAPath = !string.IsNullOrWhiteSpace(ValueOf(folder, RemotePathProperty));

        if (!createdAtAPath && PropertyOf(folder, CreateIfMissingProperty) is not null)
        {
            return (
                folder,
                $"Folder alias '{alias}' states '{CreateIfMissingProperty}' while naming no '{RemotePathProperty}', and a folder found by the role it plays is never created. Leave it out, or name the path the folder is to be created at.");
        }

        if (createdAtAPath
            && RefusedSwitch(folder, CreateIfMissingProperty, alias, "is always created where its server advertises none")
                is { } created)
        {
            return (folder, created);
        }

        folder[ExistingNameOf(folder, SynchronizeProperty)] = true;

        if (createdAtAPath)
        {
            folder[ExistingNameOf(folder, CreateIfMissingProperty)] = true;
        }

        return (folder, null);
    }

    /// <summary>Says how a replacement moved the role a folder plays, or nothing where it left it where it was.</summary>
    /// <remarks>Compared as the text each states rather than as the roles they parse to, so a role this deployment does not support is still fixed rather than free to be swapped for one it does.</remarks>
    private static string? RefusedRoleChange(string? standing, JsonObject folder, string alias)
    {
        var stated = RoleOf(folder);

        if (string.Equals(standing, stated, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return standing is null
            ? $"Folder alias '{alias.Trim()}' plays no role, and a role is stated when a folder is declared rather than given to one that was declared without it. Leave '{SpecialUseProperty}' out."
            : $"Folder alias '{alias.Trim()}' plays the '{standing}' role, and a folder's role is fixed once the folder is declared. State it as '{standing}'.";
    }

    /// <summary>Says why a switch this surface sets is not the value the client stated, or nothing where the client stated none or stated it truly.</summary>
    private static string? RefusedSwitch(JsonObject folder, string property, string alias, string rule) =>
        PropertyOf(folder, property) is { } stated && !IsTrue(stated)
            ? $"Folder alias '{alias}' states '{property}' as {stated.ToJsonString()}, and a folder declared here {rule}. State it as true, or leave it out."
            : null;

    /// <summary>Reports whether a stated value is the one the switch is set to, read as the configuration layer reads it.</summary>
    /// <remarks>
    /// By value rather than by JSON type, so the string <c>"true"</c> is the same answer as the boolean <c>true</c> and
    /// is declared rather than refused. A declaration is the object a configuration file would have written, and every
    /// provider in the pipeline flattens a file's values to strings before anything binds them — so the two spellings
    /// reach the binder identically, and refusing one of them would refuse a request asking for exactly what this
    /// surface sets. Anything that is not that value, in either spelling, is still refused.
    /// </remarks>
    private static bool IsTrue(JsonNode stated) =>
        stated is JsonValue value && string.Equals(value.ToString(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the role a folder declaration states, as the text it states it as, or nothing where it states none.</summary>
    private static string? RoleOf(JsonNode declaration) => ValueOf(declaration, SpecialUseProperty)?.Trim() is { Length: > 0 } role
        ? role
        : null;

    /// <summary>Rewrites the account's folders with whatever a change made of them.</summary>
    private static string WithFolders(JsonObject account, JsonNode[] folders)
    {
        account.Remove(ExistingNameOf(account, FoldersProperty));

        if (folders.Length > 0)
        {
            account[FoldersProperty] = KeyedByPosition(folders);
        }

        return account.ToJsonString();
    }

    private static JsonObject AccountOf(string accountJson) =>
        ObjectOf(accountJson, "The mail account's settings are not a JSON object, so there is no folder in them to change.");

    private static JsonObject FolderOf(string folderJson)
    {
        ArgumentNullException.ThrowIfNull(folderJson);

        return FolderOf(JsonNode.Parse(folderJson));
    }

    private static JsonObject FolderOf(JsonNode? declaration)
    {
        var folder = declaration as JsonObject
            ?? throw new FormatException("A folder declaration is a JSON object of that folder's settings, and this is not one.");

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
