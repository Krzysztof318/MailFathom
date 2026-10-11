// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>The record one write commits, and the endpoint switches its user is served on under the policies in force.</summary>
/// <param name="Json">The user's own record, as the JSON object the row will hold: only what somebody stated on it, never a value a policy forces.</param>
/// <param name="EndpointAccess">The switches the record resolves to through the policies governing it, which the row carries for whatever authenticates a request.</param>
public sealed record UserRecordCandidate(string Json, UserEndpointAccess EndpointAccess);
