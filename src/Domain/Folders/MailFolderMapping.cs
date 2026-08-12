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
        MailFolderSpecialUse? specialUse,
        MailFolderParticipation participation,
        bool mayCreateMissingFolder)
    {
        this.Alias = alias;
        this.Target = target;
        this.RemotePath = remotePath;
        this.SpecialUse = specialUse;
        this.Participation = participation;
        this.MayCreateMissingFolder = mayCreateMissingFolder;
    }

    /// <summary>Gets the operator-facing folder name.</summary>
    public MailFolderAlias Alias { get; }

    /// <summary>Gets which way this mapping names its remote folder.</summary>
    public MailFolderMappingTarget Target { get; }

    /// <summary>Gets the configured remote path, which is present exactly when <see cref="Target" /> is <see cref="MailFolderMappingTarget.RemotePath" />.</summary>
    public RemoteFolderPath? RemotePath { get; }

    /// <summary>Gets the configured special-use role, which is present exactly when <see cref="Target" /> is <see cref="MailFolderMappingTarget.SpecialUse" />.</summary>
    public MailFolderSpecialUse? SpecialUse { get; }

    /// <summary>Gets how far into MailFathom the mapped folder is admitted.</summary>
    /// <remarks>
    /// Resolution is deliberately indifferent to it. Which remote folder an alias names is the same question whether or
    /// not the folder is mirrored, and a mapping that resolved only while it was being synchronized could not be the
    /// destination of anything.
    /// </remarks>
    public MailFolderParticipation Participation { get; }

    /// <summary>Gets whether MailFathom may create the folder on the mail server when the account's server advertises none at the configured path.</summary>
    /// <remarks>
    /// <para>
    /// It defaults to <see langword="false" /> and is the one switch on a mapping that authorizes an act against
    /// somebody else's mail server rather than withdrawing a folder from something MailFathom does locally, which is
    /// why it does not follow the participation switches in defaulting to <see langword="true" />. A mapping that says
    /// nothing therefore behaves as it did before creation existed, and a mistyped path stays an alias that resolves to
    /// nothing rather than becoming a folder named after the mistake.
    /// </para>
    /// <para>
    /// It is expressible only alongside a configured path, because a folder that does not exist advertises no role and
    /// nothing here may invent a name for one. That is a property of the factories rather than a rule stated elsewhere:
    /// <see cref="ToSpecialUse" /> takes no such argument, so a role mapping can never carry it.
    /// </para>
    /// </remarks>
    public bool MayCreateMissingFolder { get; }

    /// <summary>Maps an alias onto the server-advertised path an operator wrote.</summary>
    /// <param name="alias">The operator-facing folder name.</param>
    /// <param name="remotePath">The remote path the alias names.</param>
    /// <param name="participation">How far the folder is admitted, or <see langword="null" /> for a folder that takes part in everything.</param>
    /// <param name="mayCreateMissingFolder">Whether the folder may be created when the server advertises none at that path.</param>
    /// <returns>A mapping resolved by matching the advertised path.</returns>
    public static MailFolderMapping ToRemotePath(
        MailFolderAlias alias,
        RemoteFolderPath remotePath,
        MailFolderParticipation? participation = null,
        bool mayCreateMissingFolder = false) => new(
            alias,
            MailFolderMappingTarget.RemotePath,
            remotePath,
            specialUse: null,
            participation ?? MailFolderParticipation.Full,
            mayCreateMissingFolder);

    /// <summary>Maps an alias onto a special-use role, so the server's own naming stays out of configuration.</summary>
    /// <param name="alias">The operator-facing folder name.</param>
    /// <param name="specialUse">The role the remote folder must carry.</param>
    /// <param name="participation">How far the folder is admitted, or <see langword="null" /> for a folder that takes part in everything.</param>
    /// <returns>A mapping resolved by matching the role.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="specialUse" /> is not a defined role.</exception>
    public static MailFolderMapping ToSpecialUse(
        MailFolderAlias alias,
        MailFolderSpecialUse specialUse,
        MailFolderParticipation? participation = null)
    {
        if (!Enum.IsDefined(specialUse))
        {
            throw new ArgumentOutOfRangeException(
                nameof(specialUse),
                specialUse,
                "A folder mapping cannot name a special-use role that does not exist.");
        }

        return new MailFolderMapping(
            alias,
            MailFolderMappingTarget.SpecialUse,
            remotePath: null,
            specialUse,
            participation ?? MailFolderParticipation.Full,
            mayCreateMissingFolder: false);
    }
}
