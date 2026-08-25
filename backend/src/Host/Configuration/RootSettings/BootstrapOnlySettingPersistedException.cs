// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Host.Configuration.RootSettings;

/// <summary>Indicates that the persisted configuration document carries a setting read before the layer is composed.</summary>
/// <remarks>
/// The message names the refused keys and never their values. A key is MailFathom's own name for a setting, in the
/// same class as an account alias; the values behind these particular keys are a connection string, a credential
/// reference, and a filesystem path the deployment chose, and a reader repairing the document is already looking at
/// them.
/// </remarks>
public sealed class BootstrapOnlySettingPersistedException : MailFathomException
{
    /// <summary>Initializes a new failure naming the persisted settings that may only come from beneath the layer.</summary>
    /// <param name="operatorSafeMessage">A message naming the keys and the correction, and no configured value.</param>
    public BootstrapOnlySettingPersistedException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.BootstrapOnlySettingPersisted;
}
