// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Configures how one folder alias finds its remote folder.</summary>
/// <remarks>
/// Configuration names folders by alias only. Either the operator writes the server's own path, or — preferably —
/// names a special-use role and lets discovery find whichever folder carries it, which is what makes an account on a
/// server with non-English folder names work with no configuration of its own.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailFolderMappingOptions : IValidatableObject
{
    /// <summary>Gets or sets the stable operator-facing folder name.</summary>
    [Required]
    public string Alias { get; set; } = string.Empty;

    /// <summary>Gets or sets the server-advertised path the alias names, which is mutually exclusive with <see cref="SpecialUse" />.</summary>
    public string? RemotePath { get; set; }

    /// <summary>Gets or sets the special-use role the alias names, which is mutually exclusive with <see cref="RemotePath" />.</summary>
    public string? SpecialUse { get; set; }

    /// <summary>Gets or sets whether the folder's mail is mirrored locally, which every mapped folder does unless it is turned off.</summary>
    /// <remarks>
    /// It is nullable so that leaving it out and writing <c>true</c> stay distinguishable. Both mirror the folder, but
    /// only the second is an operator asking for something, which is what validation needs to tell a contradiction from
    /// a default.
    /// </remarks>
    public bool? Synchronize { get; set; }

    /// <summary>Gets or sets whether the mirrored content is cut into passages and embedded.</summary>
    public bool? GenerateEmbeddings { get; set; }

    /// <summary>Gets or sets whether MCP tools may list, search, read, or answer from the folder.</summary>
    public bool? VisibleToTools { get; set; }

    /// <summary>Gets or sets whether MailFathom may create the folder when the server advertises none at <see cref="RemotePath" />.</summary>
    /// <remarks>
    /// It defaults to <see langword="false" />, unlike the three switches above, because it authorizes an act against
    /// the operator's mail server rather than withdrawing an existing folder from something MailFathom does locally.
    /// Leaving it out therefore keeps a mistyped path reporting itself as an alias that resolves to nothing.
    /// </remarks>
    public bool? CreateIfMissing { get; set; }

    /// <summary>Gets the special-use role this folder names, or <see langword="null" /> when it names a remote path instead.</summary>
    /// <remarks>
    /// Read from what the operator wrote rather than from what the server advertises, which is the only source available
    /// before a folder has ever been resolved. An account whose junk folder is configured by path therefore has no junk
    /// role here, and mail in it is listed like any other folder's — which is the honest answer, since nothing told
    /// MailFathom that the path is where junk goes.
    /// </remarks>
    internal MailFolderSpecialUse? ConfiguredSpecialUse =>
        TryParseSpecialUse(this.SpecialUse, out var specialUse) ? specialUse : null;

    /// <summary>Gets what this configured folder takes part in, with every unset switch reading as its default.</summary>
    internal MailFolderParticipation Participation => MailFolderParticipation.Create(
        this.Synchronize ?? true,
        this.GenerateEmbeddings ?? true,
        this.VisibleToTools ?? true);

    /// <summary>Builds the domain mapping this configured folder expresses.</summary>
    /// <returns>The mapping folder resolution reads.</returns>
    /// <exception cref="InvalidOperationException">Thrown when configuration that passed startup validation no longer expresses exactly one target.</exception>
    internal MailFolderMapping CreateMapping()
    {
        var alias = MailFolderAlias.Create(this.Alias);

        if (!string.IsNullOrWhiteSpace(this.RemotePath))
        {
            return MailFolderMapping.ToRemotePath(
                alias,
                RemoteFolderPath.Create(this.RemotePath),
                this.Participation,
                this.CreateIfMissing ?? false);
        }

        if (TryParseSpecialUse(this.SpecialUse, out var specialUse))
        {
            return MailFolderMapping.ToSpecialUse(alias, specialUse, this.Participation);
        }

        throw new InvalidOperationException(
            $"Folder alias '{this.Alias}' names neither a remote path nor a supported special-use role.");
    }

    internal IEnumerable<ValidationResult> ValidateForSynchronization()
    {
        if (string.IsNullOrWhiteSpace(this.Alias))
        {
            yield return new ValidationResult("Configured folder aliases must be non-empty.", [nameof(this.Alias)]);
        }

        var namesRemotePath = !string.IsNullOrWhiteSpace(this.RemotePath);
        var namesSpecialUse = !string.IsNullOrWhiteSpace(this.SpecialUse);

        if (namesRemotePath == namesSpecialUse)
        {
            yield return new ValidationResult(
                $"Folder alias '{this.Alias}' must name exactly one of RemotePath and SpecialUse.",
                [nameof(this.RemotePath), nameof(this.SpecialUse)]);

            yield break;
        }

        if (namesSpecialUse && !TryParseSpecialUse(this.SpecialUse, out _))
        {
            yield return new ValidationResult(
                $"Folder alias '{this.Alias}' names special-use role '{this.SpecialUse}', which is not supported. Supported roles are {string.Join(", ", Enum.GetNames<MailFolderSpecialUse>())}.",
                [nameof(this.SpecialUse)]);
        }

        if (namesSpecialUse && this.CreateIfMissing is true)
        {
            yield return new ValidationResult(
                $"Folder alias '{this.Alias}' asks for its folder to be created while naming a special-use role, and a folder that does not exist advertises no role. Name the path the folder is to be created at in 'RemotePath'.",
                [nameof(this.CreateIfMissing)]);
        }

        foreach (var result in this.ValidateConfiguredValues(namesRemotePath))
        {
            yield return result;
        }

        foreach (var result in this.ValidateParticipation())
        {
            yield return result;
        }
    }

    /// <summary>Refuses a folder asked to do something an unmirrored folder cannot do.</summary>
    /// <remarks>
    /// Nothing here is a safety rule — the participation value already withdraws both from an unsynchronized folder, so
    /// the configuration would work. What it would not do is what it says, and a folder configured to embed mail nobody
    /// stores is an operator expecting a bill and a search result they will never get. The unset case is deliberately
    /// not refused: leaving a switch out is not asking for it.
    /// </remarks>
    private IEnumerable<ValidationResult> ValidateParticipation()
    {
        if (this.Synchronize is not false)
        {
            yield break;
        }

        if (this.GenerateEmbeddings is true)
        {
            yield return new ValidationResult(
                $"Folder alias '{this.Alias}' asks for embeddings while 'Synchronize' is false, and an unsynchronized folder stores no content to embed.",
                [nameof(this.GenerateEmbeddings)]);
        }

        if (this.VisibleToTools is true)
        {
            yield return new ValidationResult(
                $"Folder alias '{this.Alias}' asks to be visible to tools while 'Synchronize' is false, and an unsynchronized folder stores nothing a tool could read.",
                [nameof(this.VisibleToTools)]);
        }
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => this.ValidateForSynchronization();

    /// <summary>Parses a configured role name, rejecting the numeric form a configuration value can otherwise bind to.</summary>
    private static bool TryParseSpecialUse(string? configuredRole, out MailFolderSpecialUse specialUse)
    {
        specialUse = default;

        return !string.IsNullOrWhiteSpace(configuredRole)
            && !configuredRole.Trim().Any(char.IsDigit)
            && Enum.TryParse(configuredRole.Trim(), ignoreCase: true, out specialUse)
            && Enum.IsDefined(specialUse);
    }

    /// <summary>Re-checks the alias and path against the domain rules, so an unusable value fails startup rather than the first run.</summary>
    private IEnumerable<ValidationResult> ValidateConfiguredValues(bool namesRemotePath)
    {
        if (!string.IsNullOrWhiteSpace(this.Alias) && !TryCreate(() => _ = MailFolderAlias.Create(this.Alias), out var aliasFailure))
        {
            yield return new ValidationResult(
                $"Folder alias '{this.Alias}' is not usable: {aliasFailure}",
                [nameof(this.Alias)]);
        }

        if (namesRemotePath && !TryCreate(() => _ = RemoteFolderPath.Create(this.RemotePath!), out var pathFailure))
        {
            yield return new ValidationResult(
                $"Folder alias '{this.Alias}' names a remote path that is not usable: {pathFailure}",
                [nameof(this.RemotePath)]);
        }
    }

    /// <summary>Turns a domain value object's rejection into a startup message, which is the one place it is expected.</summary>
    private static bool TryCreate(Action create, out string failure)
    {
        try
        {
            create();
            failure = string.Empty;

            return true;
        }
        catch (ArgumentException exception)
        {
            failure = exception.Message;

            return false;
        }
    }
}
