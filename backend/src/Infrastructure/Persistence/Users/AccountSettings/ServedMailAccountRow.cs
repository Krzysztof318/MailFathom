// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Synchronization;

namespace MailFathom.Infrastructure.Persistence.Users.AccountSettings;

/// <summary>One account this deployment serves, as the columns that name it.</summary>
/// <param name="Id">The identifier the deployment generated for the account.</param>
/// <param name="DisplayName">The name the account is told apart by.</param>
/// <param name="SynchronizationMode">How the account asked to be synchronized.</param>
public sealed record ServedMailAccountRow(Guid Id, string DisplayName, MailSynchronizationMode SynchronizationMode);
