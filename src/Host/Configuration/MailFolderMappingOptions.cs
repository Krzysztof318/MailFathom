// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration;

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

    /// <summary>Builds the domain mapping this configured folder expresses.</summary>
    /// <returns>The mapping folder resolution reads.</returns>
    /// <exception cref="InvalidOperationException">Thrown when configuration that passed startup validation no longer expresses exactly one target.</exception>
    internal MailFolderMapping CreateMapping()
    {
        var alias = MailFolderAlias.Create(this.Alias);

        if (!string.IsNullOrWhiteSpace(this.RemotePath))
        {
            return MailFolderMapping.ToRemotePath(alias, RemoteFolderPath.Create(this.RemotePath));
        }

        if (TryParseSpecialUse(this.SpecialUse, out var specialUse))
        {
            return MailFolderMapping.ToSpecialUse(alias, specialUse);
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

        foreach (var result in this.ValidateConfiguredValues(namesRemotePath))
        {
            yield return result;
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
