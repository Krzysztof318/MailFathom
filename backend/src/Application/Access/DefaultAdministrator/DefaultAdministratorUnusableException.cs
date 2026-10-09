// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Access.DefaultAdministrator;

/// <summary>Stops a start that cannot give the default administrator the password it was started with.</summary>
/// <remarks>
/// A refusal rather than a warning, because both cases leave the deployment's first sign-in wrong in a way the operator
/// would not see: a password they believe they set that was never applied, or a password applied to whoever already
/// signs in as <c>admin</c>. Neither message names the password.
/// </remarks>
public sealed class DefaultAdministratorUnusableException : MailFathomException
{
    private DefaultAdministratorUnusableException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.DefaultAdministratorUnusable;

    /// <summary>Describes a password setting carrying a value the password policy refuses.</summary>
    /// <param name="variableName">The environment variable the value was read from.</param>
    /// <param name="refusal">What the policy refused, which never quotes the value.</param>
    /// <returns>The exception.</returns>
    public static DefaultAdministratorUnusableException PasswordRefused(string variableName, string refusal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(refusal);

        return new(
            $"{variableName} carries a password the password policy refuses, so the default administrator '{DefaultAdministratorBootstrap.Username}' "
            + $"would be left without the password it was started with. {refusal} The shipped value "
            + $"'{DefaultAdministratorBootstrap.ShippedPassword}' is the one exemption. Set a value the policy accepts, or unset the variable.");
    }

    /// <summary>Describes a username the default administrator's password would collide with.</summary>
    /// <returns>The exception.</returns>
    public static DefaultAdministratorUnusableException UsernameTaken() => new(
        $"A password credential signed in as '{DefaultAdministratorBootstrap.Username}' in no organization belongs to another user, so applying "
        + "the default administrator's password would either fail or sign that user in as the administrator. Remove that "
        + "credential with 'mfctl credential delete' and provision that user's password again under another username "
        + "with 'mfctl credential create', then start again.");
}
