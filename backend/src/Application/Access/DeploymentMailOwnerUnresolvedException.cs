// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Access;

/// <summary>Indicates that this deployment does not hold the single owner its configured mail accounts belong to.</summary>
/// <remarks>
/// <para>
/// Raised rather than returned, because no caller above it can decide what it means. A configured mail account names no
/// owner, so a deployment holding no owner record serves accounts belonging to nobody, and one holding several cannot
/// say which of them a configured account is for. Neither is a state a request can be answered in: the accounts a caller
/// would be shown are decided by whose they are, so a host that could not settle the question refuses to finish starting
/// rather than serving a reader whose bound was guessed.
/// </para>
/// <para>
/// The message names the count and the remedy and nothing else. An owner identity is a generated identifier that names a
/// person inside this deployment, so no message here carries one.
/// </para>
/// </remarks>
public sealed class DeploymentMailOwnerUnresolvedException : MailFathomException
{
    private DeploymentMailOwnerUnresolvedException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.DeploymentMailOwnerUnresolved;

    /// <summary>Reports a deployment holding no owner record at all.</summary>
    /// <returns>The failure to raise.</returns>
    public static DeploymentMailOwnerUnresolvedException NoOwner() => new(
        "This deployment holds no owner record, so the mail accounts it is configured with belong to nobody and no "
        + "caller could be admitted to act for one. Apply the schema of this release, which provisions the owner an "
        + "upgraded deployment's accounts are carried onto.");

    /// <summary>Reports a deployment holding more than one owner record while its accounts are still configured.</summary>
    /// <returns>The failure to raise.</returns>
    public static DeploymentMailOwnerUnresolvedException SeveralOwners() => new(
        "This deployment holds more than one owner record while its mail accounts are declared in configuration, which "
        + "names no owner, so a configured account cannot be attributed to one of them. Serve one owner per deployment "
        + "until accounts are declared in the owner record that is to own them.");
}
