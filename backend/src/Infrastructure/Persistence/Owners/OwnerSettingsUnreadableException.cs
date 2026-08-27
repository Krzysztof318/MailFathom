// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Indicates that one owner's persisted record could not be read.</summary>
/// <remarks>
/// <para>
/// An owner this deployment holds no record of is not this: that is an absence the reader reports as one, because a
/// caller acting for somebody who was never provisioned is an ordinary answer. What this covers is a row that is
/// there and cannot be handed on — today, a document past what this build binds, which the statement refuses rather
/// than transfers.
/// </para>
/// <para>
/// The message names the owner's identifier and the limit, and never the document beside it, which is that person's
/// configuration rather than a diagnostic.
/// </para>
/// </remarks>
public sealed class OwnerSettingsUnreadableException : MailFathomException
{
    /// <summary>Initializes a new failure naming what could not be read and what to do about it.</summary>
    /// <param name="operatorSafeMessage">A message naming the owner's record and the operator's next step.</param>
    public OwnerSettingsUnreadableException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.OwnerSettingsUnreadable;
}
