// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Access.Credentials;

/// <summary>Indicates that a client assertion could not be recorded as served, so the request presenting it was refused.</summary>
/// <remarks>
/// <para>
/// It exists so the failure crosses <see cref="IClientAssertionSpendStore" /> as this system's own rather than as the
/// provider's. The port is an application contract and the caller is an authentication handler, so an adapter's
/// exception reaching it would put a database driver's type and a server's own message on the path that answers an
/// unauthenticated caller.
/// </para>
/// <para>
/// One failure covers a database that could not be reached, a statement that timed out, and a value the server refused,
/// because all three are the same thing to the layer above: the deployment cannot say whether this identifier has been
/// served. What that layer does about it is refuse, which is why the distinction between them belongs in
/// <see cref="Exception.InnerException" /> for an operator's log rather than in a code the caller would branch on.
/// </para>
/// <para>
/// The message names the record and the operator's next step. It carries no connection string, no credential, no
/// identifier a client minted, and nothing the server said, all of which stay reachable through the inner exception.
/// </para>
/// </remarks>
public sealed class ClientAssertionSpendUnrecordableException : MailFathomException
{
    /// <summary>Initializes a new failure over the provider failure that revealed it.</summary>
    /// <param name="operatorSafeMessage">A message free of provider details, credentials, and client-minted values.</param>
    /// <param name="providerFailure">The provider failure the statement met.</param>
    public ClientAssertionSpendUnrecordableException(string operatorSafeMessage, Exception providerFailure)
        : base(operatorSafeMessage, providerFailure)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.ClientAssertionSpendUnrecordable;
}
