// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.OwnerSettings;

/// <summary>Names where one owner's mail accounts and own settings are read from.</summary>
/// <remarks>
/// The distinction is per owner rather than per deployment, which is the whole point of it: a deployment routinely
/// holds owners read from a file beside owners who have taken their record over, and each of them is served from the
/// source their own row says. What a start reports is this value for every owner it serves, because a section somebody
/// goes on editing for an owner that no longer reads it is the failure the report exists to prevent.
/// </remarks>
internal enum MailOwnerAccountSource
{
    /// <summary>The deployment's own <c>MailSynchronization:Accounts</c>, which names no owner and therefore belongs to the sole owner such a deployment holds.</summary>
    DeploymentSection = 0,

    /// <summary>The owner's own section of the top-level <c>Accounts</c> collection, which is where a file declaring several owners puts each one's mailboxes.</summary>
    OwnerDeclaration = 1,

    /// <summary>The owner's own document, which is the source from the moment an adoption writes it and permanently afterwards.</summary>
    OwnerDocument = 2,
}
