// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>The mail accounts one listing read, and whether the deployment holds more than it could list.</summary>
/// <param name="Accounts">The accounts and the users each is assigned to, in the order they were created in.</param>
/// <param name="Truncated">Whether the deployment holds more accounts than one listing reads.</param>
internal sealed record MailAccountListing(IReadOnlyList<MailAccountHolding> Accounts, bool Truncated);
