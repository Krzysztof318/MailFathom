// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>Resolves who each account's outgoing mail is written from.</summary>
/// <remarks>
/// It is a reader of its own rather than a member of the submission settings provider, because the two answer questions
/// about different things and are wanted at different moments. Where mail is submitted and which credential
/// authenticates are the connection's, resolved when one is about to be opened and holding secret material while they
/// are; who the mail is from is the message's, wanted while it is being composed and holding nothing that needs
/// erasing.
/// </remarks>
public interface IOutgoingSenderIdentityReader
{
    /// <summary>Gets the identity one account sends as.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <returns>The identity, or <see langword="null" /> when the account is no longer configured or configures no address to send from.</returns>
    /// <remarks>
    /// Absence is an ordinary answer for both reasons, so a caller composes nothing rather than raising: an account a
    /// reload removed and an account whose submission endpoint was written without a sending address both leave nothing
    /// to write a <c>From</c> header from, and neither is something the composition can repair.
    /// </remarks>
    OutgoingSenderIdentity? FindSenderIdentity(MailAccountId accountId);
}
