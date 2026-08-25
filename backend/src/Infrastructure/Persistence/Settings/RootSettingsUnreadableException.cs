// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Infrastructure.Persistence.Settings;

/// <summary>Indicates that the deployment's persisted configuration could not be read.</summary>
/// <remarks>
/// <para>
/// One failure covers a database that could not be reached, a schema that does not carry the table yet, and a row that
/// is not there, because all three are the same thing to the layer above: the configuration source below the
/// operator's overrides cannot say what it contributes. Starting anyway would serve whichever values the files beneath
/// it happen to carry, with nothing in the process saying that a layer was missing.
/// </para>
/// <para>
/// The message names the table and what an operator does about it. It never carries a connection string, a credential,
/// or any part of the document, which is settings rather than a diagnostic.
/// </para>
/// </remarks>
public sealed class RootSettingsUnreadableException : MailFathomException
{
    /// <summary>Initializes a new failure naming what could not be read and what to do about it.</summary>
    /// <param name="operatorSafeMessage">A message naming the persisted configuration and the operator's next step.</param>
    public RootSettingsUnreadableException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <summary>Initializes a new failure naming what could not be read, and the provider failure that revealed it.</summary>
    /// <param name="operatorSafeMessage">A message naming the persisted configuration and the operator's next step.</param>
    /// <param name="innerException">The provider failure this was raised for.</param>
    public RootSettingsUnreadableException(string operatorSafeMessage, Exception innerException)
        : base(operatorSafeMessage, innerException)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.RootSettingsUnreadable;
}
