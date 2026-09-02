// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Rules;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Refuses a junk destination one of the configured accounts could never file into.</summary>
/// <remarks>
/// The accounts are read straight from configuration rather than from the bound synchronization options, so that a defect
/// in that section is reported by that section rather than raised out of this one. Reading the keys is also what makes
/// the answer the same at startup and on every reload, since both see the same text.
/// </remarks>
internal sealed class SpamJunkFolderValidator(IConfiguration configuration)
    : IValidateOptions<SpamClassificationOptions>
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options" /> is <see langword="null" />.</exception>
    public ValidateOptionsResult Validate(string? name, SpamClassificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = SpamJunkFolderRules
            .FindDestinationErrors(options, DeclaredMailAccounts.ReadFrom(configuration))
            .Select(result => result.ErrorMessage ?? string.Empty)
            .ToArray();

        return failures.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
