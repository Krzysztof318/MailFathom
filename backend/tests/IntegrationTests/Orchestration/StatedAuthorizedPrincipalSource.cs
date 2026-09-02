// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Reports whoever a unit of work this suite drives is running for, which is MailFathom itself until a test says otherwise.</summary>
/// <remarks>
/// <para>
/// A composition root supplies this port from the request being served, and this suite starts no request: it composes
/// the production registrations and calls the classes under them directly, which is the same thing a worker does. The
/// answer is therefore the process identity, so a use case that requires a permission is refused here exactly as it
/// would be in a worker — which is the behaviour a test would want to meet rather than one to arrange around.
/// </para>
/// <para>
/// A use case an agent reaches is the exception, because the work a caller asks for is refused under that identity and
/// there is no request here to carry a credential. Such a test states the caller for its own scope, the way the host's
/// own adapter lets a route state one it verified, and the statement is scoped exactly as the source is.
/// </para>
/// </remarks>
internal sealed class StatedAuthorizedPrincipalSource : IAuthorizedPrincipalSource
{
    /// <inheritdoc />
    public AuthorizedPrincipal? Current { get; private set; } = AuthorizedPrincipal.Process;

    /// <summary>States the caller this scope's work is running for.</summary>
    /// <param name="principal">Whoever the test is acting as.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="principal" /> is <see langword="null" />.</exception>
    internal void Assume(AuthorizedPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        this.Current = principal;
    }
}
