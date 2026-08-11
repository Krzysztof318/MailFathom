// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.SensitiveContent.Detection;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration.SensitiveContent;

/// <summary>Refuses a scanning configuration that names something no registered scanner detects.</summary>
/// <remarks>
/// The rule cannot be an attribute or an <see cref="System.ComponentModel.DataAnnotations.IValidatableObject" /> member
/// on the bound graph, because the answer depends on which scanners this deployment registered rather than on anything
/// the section says about itself. The options framework's own validator seam is what supplies them, and registering it
/// runs the rule at startup through <c>ValidateOnStart</c> — which is the point of it, since a scanner switched on with
/// nothing behind it must stop the service rather than surface later as content nobody scanned.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework resolves this validator through IValidateOptions.")]
internal sealed class SensitiveContentCatalogValidator(IEnumerable<ISensitiveContentCatalog> catalogs)
    : IValidateOptions<SensitiveContentOptions>
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options" /> is <see langword="null" />.</exception>
    public ValidateOptionsResult Validate(string? name, SensitiveContentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = SensitiveContentDeclarationRules
            .FindDeclarationErrors(options, catalogs)
            .Select(result => result.ErrorMessage ?? string.Empty)
            .ToArray();

        return failures.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
