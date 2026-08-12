// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration.Rules;

/// <summary>One folder as a rule set is judged against it: the name it carries and the role it plays.</summary>
/// <param name="Alias">MailFathom's own name for the folder.</param>
/// <param name="Role">The role the folder plays, or <see langword="null" /> when configuration labelled it with none.</param>
internal readonly record struct DeclaredMailFolder(MailFolderAlias Alias, MailFolderSpecialUse? Role)
{
    /// <summary>Reports whether a reference names this folder.</summary>
    /// <param name="reference">The alias or role something wrote.</param>
    /// <returns><see langword="true" /> when this folder is the one that reference means.</returns>
    internal bool IsNamedBy(MailFolderReference reference) => reference switch
    {
        { Alias: { } alias } => this.Alias == alias,
        { Role: { } role } => this.Role == role,
        _ => false,
    };
}
