// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Access;

/// <summary>The administrator one token identity admits, and the scopes the credential naming it asks of a token.</summary>
/// <param name="Administrator">The administrator the issuer and subject were written under.</param>
/// <param name="RequiredScopes">The scopes the OAuth credential naming the subject requires.</param>
internal sealed record AdministratorTokenBinding(
    AdministratorOptions Administrator,
    IReadOnlyCollection<string> RequiredScopes);
