// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Signals;

/// <summary>Indicates that the deployment's signal tickets could not be reached, so the connection or the mint was refused.</summary>
/// <remarks>
/// <para>
/// It exists so the failure crosses <see cref="IClientSignalTicketStore" /> as this system's own rather than as the
/// provider's, for the reason the anti-replay record's does: the port is an application contract and one of its callers
/// is a hub that has authenticated nothing yet, so an adapter's exception reaching there would put a database driver's
/// type and a server's own message on the path that answers an unauthenticated caller.
/// </para>
/// <para>
/// One failure covers a database that could not be reached, a statement that timed out, and a value the server refused,
/// because all three are the same thing to the layer above: the deployment cannot say whether this ticket stands. What
/// that layer does about it is refuse — a connection admitted on a store that could not answer is a connection opened
/// against no ticket at all — which is why the distinction between them belongs in <see cref="Exception.InnerException" />
/// for an operator's log rather than in a code the caller would branch on.
/// </para>
/// <para>
/// The message names the record and the operator's next step. It carries no connection string, no ticket, and nothing
/// the server said, all of which stay reachable through the inner exception.
/// </para>
/// </remarks>
public sealed class ClientSignalTicketStoreUnavailableException : MailFathomException
{
    /// <summary>Initializes a new failure over the provider failure that revealed it.</summary>
    /// <param name="operatorSafeMessage">A message free of provider details, credentials, and client-presented values.</param>
    /// <param name="providerFailure">The provider failure the statement met.</param>
    public ClientSignalTicketStoreUnavailableException(string operatorSafeMessage, Exception providerFailure)
        : base(operatorSafeMessage, providerFailure)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.ClientSignalTicketStoreUnavailable;
}
