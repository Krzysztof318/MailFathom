// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>What a write to one user's endpoint switches did, beside the switches their record states afterwards.</summary>
/// <param name="Outcome">What the write to the record did.</param>
/// <param name="EndpointAccess">Both switches as the record now states them, including one the write left out; the ones it stood at where the write was refused, or the default reaching neither endpoint where the standing switches could not be read at all.</param>
internal sealed record UserEndpointAccessWrite(UserRecordWriteOutcome Outcome, MailUserEndpointAccess EndpointAccess);
