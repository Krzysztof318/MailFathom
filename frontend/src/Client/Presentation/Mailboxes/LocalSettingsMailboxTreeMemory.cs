// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Presentation.Workspace;

namespace MailFathom.Client.Presentation.Mailboxes;

/// <summary>Keeps the tree's arrangement in the settings store the platform already has.</summary>
/// <remarks>
/// <para>
/// <see cref="ApplicationData.LocalSettings" /> rather than a file this application invents, for the reason the chosen
/// deployment is kept there: every head already has one and each maps it to what that platform actually uses — a
/// per-user preferences store on a desktop and the browser's own storage for the page's origin in the browser head. So
/// "per user" and "survives a restart" are the platform's guarantees rather than something written here.
/// </para>
/// <para>
/// The values are written as plain text rather than as anything serialized, because a settings store holds simple
/// values and the browser head publishes trimmed. The expanded keys are one entry joined by line feeds, which is a
/// character no key composed here can contain — a key joins its own parts with a unit separator — so reading them back
/// is a split rather than a parse. An entry that is no longer readable is treated as nothing having been remembered,
/// which is the same answer a first run gives and is never a failure to start.
/// </para>
/// <para>
/// Nothing here is written for another deployment's benefit. A remembered mailbox that this deployment does not serve
/// simply matches no row, and the tree drops the selection rather than claiming to be somewhere it is not.
/// </para>
/// </remarks>
internal sealed class LocalSettingsMailboxTreeMemory : IMailboxTreeMemory
{
    /// <summary>The name the narrowed account is kept under.</summary>
    /// <remarks>Qualified, because the container is shared with every other setting this application and its framework keep.</remarks>
    internal const string AccountSettingName = "MailFathom.Mailboxes.Account";

    /// <summary>The name the narrowed folder is kept under.</summary>
    internal const string FolderSettingName = "MailFathom.Mailboxes.Folder";

    /// <summary>The name the narrowed role is kept under.</summary>
    internal const string RoleSettingName = "MailFathom.Mailboxes.Role";

    /// <summary>The name the expanded rows are kept under, as one entry.</summary>
    internal const string ExpandedSettingName = "MailFathom.Mailboxes.Expanded";

    private const char ExpandedSeparator = '\n';

    /// <inheritdoc />
    public RememberedMailboxes Read()
    {
        var scope = new WorkspaceScope
        {
            Account = Kept(AccountSettingName),
            Folder = Kept(FolderSettingName),
            Role = OfferedRole(),
        };

        var expanded = Kept(ExpandedSettingName) is { } written
            ? written.Split(ExpandedSeparator, StringSplitOptions.RemoveEmptyEntries).ToImmutableHashSet(StringComparer.Ordinal)
            : ImmutableHashSet<string>.Empty;

        return new RememberedMailboxes(scope, expanded);
    }

    /// <inheritdoc />
    public void Write(RememberedMailboxes remembered)
    {
        ArgumentNullException.ThrowIfNull(remembered);

        Keep(AccountSettingName, remembered.Scope.Account);
        Keep(FolderSettingName, remembered.Scope.Folder);
        Keep(RoleSettingName, remembered.Scope.Role);
        Keep(ExpandedSettingName, string.Join(ExpandedSeparator, remembered.Expanded));
    }

    /// <summary>Reads the kept role, forgetting one this build does not offer rather than restoring a scope nothing can draw.</summary>
    private static string? OfferedRole() =>
        Kept(RoleSettingName) is { } kept && MailboxWords.IsOfferedRole(kept) ? kept : null;

    private static string? Kept(string name) =>
        ApplicationData.Current.LocalSettings.Values.TryGetValue(name, out var kept) && kept is string written
        && !string.IsNullOrEmpty(written)
            ? written
            : null;

    /// <summary>Keeps a value, removing the entry rather than writing an empty one where there is nothing to keep.</summary>
    /// <remarks>Removing keeps the store the shape a fresh installation has, so a scope somebody widened back to everything reads on the next start exactly as one nobody ever narrowed.</remarks>
    private static void Keep(string name, string? value)
    {
        var settings = ApplicationData.Current.LocalSettings.Values;

        if (string.IsNullOrEmpty(value))
        {
            settings.Remove(name);

            return;
        }

        settings[name] = value;
    }
}
