// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Access.Sessions;

/// <summary>Indicates that the deployment's client sessions could not be reached, so the request was refused as unavailable rather than as unauthenticated.</summary>
/// <remarks>
/// <para>
/// It exists so the failure crosses <see cref="IClientSessionStore" /> as this system's own rather than as the
/// provider's, which is the arrangement <see cref="Credentials.ClientAssertionSpendUnrecordableException" /> is under
/// and for the same reason: the port is an application contract and its callers include an authentication handler that
/// has admitted nobody yet, so a driver's exception reaching there would put a database type and a server's message on
/// the path answering an unauthenticated caller.
/// </para>
/// <para>
/// <b>What the caller does about it is refuse as unavailable, never as unauthenticated.</b> A client meets <c>401</c>
/// by clearing what it holds and asking for a password, so answering a database that could not be reached that way
/// would turn a blip into a deployment-wide sign-out and lose every session the outage covered. The distinction is the
/// whole reason this failure is raised rather than read as an unknown session.
/// </para>
/// </remarks>
public sealed class ClientSessionStoreUnavailableException : MailFathomException
{
    /// <summary>Initializes a new failure over the provider failure that revealed it.</summary>
    /// <param name="operatorSafeMessage">A message free of provider details, credentials, and client-presented values.</param>
    /// <param name="providerFailure">The provider failure the statement met.</param>
    public ClientSessionStoreUnavailableException(string operatorSafeMessage, Exception providerFailure)
        : base(operatorSafeMessage, providerFailure)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.ClientSessionStoreUnavailable;
}
