// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>The signed-in user's mail accounts as the settings policies governing them resolve each.</summary>
/// <param name="Accounts">Every account assigned to the user, by the identifier the deployment generated for it.</param>
/// <param name="NewAccount">What an account the user declared now would start from: what each property resolves to where a declaration states nothing, and which of them are the user's to state.</param>
/// <remarks>
/// The second half is what lets a client fill in what an organization already decided before the person has typed
/// anything: a forced host is shown as held, and a default port is offered as the value a declaration may leave out.
/// </remarks>
internal sealed record OwnMailAccountsPolicyReading(
    IReadOnlyDictionary<Guid, MailAccountPolicyReading> Accounts,
    MailAccountPolicyReading NewAccount);
