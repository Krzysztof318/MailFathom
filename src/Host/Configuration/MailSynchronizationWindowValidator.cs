// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;

namespace MailMcp.Host.Configuration;

/// <summary>Refuses a configured synchronization bound that the current date makes meaningless.</summary>
/// <remarks>
/// Every other mail synchronization rule is a data annotation or an <see cref="IValidatableObject" /> rule on the bound
/// options graph, and this one cannot be either: whether an earliest received date is in the future is a question about
/// the current date, which neither an attribute nor a bound object can reach. It is therefore the options framework's
/// own custom-validator seam that supplies the clock, and the rule itself stays in
/// <see cref="MailSynchronizationOptions" /> with the rules it belongs beside. Registering it runs it at startup
/// through <c>ValidateOnStart</c>, and afterwards whenever a reload materializes new options, on the same terms as the
/// section's data annotations.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework resolves this validator through IValidateOptions.")]
internal sealed class MailSynchronizationWindowValidator(TimeProvider timeProvider)
    : IValidateOptions<MailSynchronizationOptions>
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options" /> is <see langword="null" />.</exception>
    public ValidateOptionsResult Validate(string? name, MailSynchronizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var failures = options
            .FindSynchronizationWindowErrors(today)
            .Select(result => result.ErrorMessage ?? string.Empty)
            .ToArray();

        return failures.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
