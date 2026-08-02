// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Expresses how an operator wants one alias to find its remote folder.</summary>
/// <remarks>
/// A mapping is the whole of what configuration says about a folder. Which remote folder it currently names is
/// discovery's answer, not the operator's, which is why nothing here carries a generation or a resolved path.
/// </remarks>
public sealed record MailFolderMapping
{
    private MailFolderMapping(
        MailFolderAlias alias,
        MailFolderMappingTarget target,
        RemoteFolderPath? remotePath,
        MailFolderSpecialUse? specialUse)
    {
        this.Alias = alias;
        this.Target = target;
        this.RemotePath = remotePath;
        this.SpecialUse = specialUse;
    }

    /// <summary>Gets the operator-facing folder name.</summary>
    public MailFolderAlias Alias { get; }

    /// <summary>Gets which way this mapping names its remote folder.</summary>
    public MailFolderMappingTarget Target { get; }

    /// <summary>Gets the configured remote path, which is present exactly when <see cref="Target" /> is <see cref="MailFolderMappingTarget.RemotePath" />.</summary>
    public RemoteFolderPath? RemotePath { get; }

    /// <summary>Gets the configured special-use role, which is present exactly when <see cref="Target" /> is <see cref="MailFolderMappingTarget.SpecialUse" />.</summary>
    public MailFolderSpecialUse? SpecialUse { get; }

    /// <summary>Maps an alias onto the server-advertised path an operator wrote.</summary>
    /// <param name="alias">The operator-facing folder name.</param>
    /// <param name="remotePath">The remote path the alias names.</param>
    /// <returns>A mapping resolved by matching the advertised path.</returns>
    public static MailFolderMapping ToRemotePath(MailFolderAlias alias, RemoteFolderPath remotePath) =>
        new(alias, MailFolderMappingTarget.RemotePath, remotePath, specialUse: null);

    /// <summary>Maps an alias onto a special-use role, so the server's own naming stays out of configuration.</summary>
    /// <param name="alias">The operator-facing folder name.</param>
    /// <param name="specialUse">The role the remote folder must carry.</param>
    /// <returns>A mapping resolved by matching the role.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="specialUse" /> is not a defined role.</exception>
    public static MailFolderMapping ToSpecialUse(MailFolderAlias alias, MailFolderSpecialUse specialUse)
    {
        if (!Enum.IsDefined(specialUse))
        {
            throw new ArgumentOutOfRangeException(
                nameof(specialUse),
                specialUse,
                "A folder mapping cannot name a special-use role that does not exist.");
        }

        return new MailFolderMapping(alias, MailFolderMappingTarget.SpecialUse, remotePath: null, specialUse);
    }
}
