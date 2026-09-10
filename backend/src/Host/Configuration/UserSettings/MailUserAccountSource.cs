// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Names where one user's mail accounts and own settings are read from.</summary>
/// <remarks>
/// The distinction is per user rather than per deployment, which is the whole point of it: a deployment routinely
/// holds users read from a file beside users who have taken their record over, and each of them is served from the
/// source their own row says. What a start reports is this value for every user it serves, because a section somebody
/// goes on editing for a user that no longer reads it is the failure the report exists to prevent.
/// </remarks>
internal enum MailUserAccountSource
{
    /// <summary>The deployment's own <c>MailSynchronization:Accounts</c>, which names no user and therefore belongs to the sole user such a deployment holds.</summary>
    DeploymentSection = 0,

    /// <summary>The user's own section of the top-level <c>Accounts</c> collection, which is where a file declaring several users puts each one's mailboxes.</summary>
    UserDeclaration = 1,

    /// <summary>The user's own document, which is the source from the moment a committed record writes it and permanently afterwards.</summary>
    UserDocument = 2,
}
