// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Host.Configuration;

/// <summary>Indicates that the deployment's configuration-source settings do not describe configuration MailFathom can load.</summary>
/// <remarks>
/// <para>
/// One failure covers a path that is not there and a setting name MailFathom does not define, because both are the same
/// mistake seen from two sides: the deployment believes it provisioned configuration and the host would not read it.
/// Nothing acts differently on the two, so splitting them would publish an identity no caller distinguishes.
/// </para>
/// <para>
/// The failure exists so that either mistake stops the process instead of leaving it running on defaults. A host that
/// silently ignored an absent mount or a misspelled key would report success while serving configuration nobody wrote,
/// and the divergence would only be discovered later, through behavior rather than through a message.
/// </para>
/// <para>
/// The message names the configuration key and, where one is involved, the path. Both are safe to publish: they are
/// values the operator wrote into their own deployment, in the same class as an account alias or a folder alias. This
/// is deliberately unlike a secret reference target, which names where credential material lives and is therefore kept
/// out of every diagnostic; a configuration path names where non-secret settings live and is useless without them.
/// </para>
/// </remarks>
public sealed class ProvisionedConfigurationSourceInvalidException : MailFathomException
{
    /// <summary>Initializes a new failure naming the configuration key that does not describe loadable configuration.</summary>
    /// <param name="operatorSafeMessage">A message naming the configuration key and, where one is involved, the path.</param>
    public ProvisionedConfigurationSourceInvalidException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.ProvisionedConfigurationSourceInvalid;
}
