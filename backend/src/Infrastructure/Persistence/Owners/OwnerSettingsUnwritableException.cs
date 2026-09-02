// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Indicates that the database refused the statement a validated owner-record write would have committed.</summary>
/// <remarks>
/// <para>
/// Raised rather than returned for the reason <c>RootSettingsUnwritableException</c> is: no caller between the
/// statement and the administrator can decide what a refused connection, a missing privilege, or a statement that
/// outran its bound means, and putting it in the same list as "the record is invalid" and "somebody else won the race"
/// would mix a failure of the machinery with two outcomes an operator's own edit produced.
/// </para>
/// <para>
/// The owner's record is unchanged when this is raised, with two exceptions the message names where they apply: a
/// statement that outran its command timeout and one whose connection broke while it was in flight had both reached
/// the server, so which of them happened is not known from here. Reading the version now in force settles it, and a
/// second attempt is safe either way, because the version guard refuses one composed over a version the first attempt
/// already moved.
/// </para>
/// <para>
/// The message names neither the connection, the credential, nor any part of the document, which is one person's own
/// configuration rather than a diagnostic. What the database said stays reachable as
/// <see cref="Exception.InnerException" /> for an operator's log.
/// </para>
/// </remarks>
public sealed class OwnerSettingsUnwritableException : MailFathomException
{
    /// <summary>Initializes a new failure naming what could not be written, and the provider failure that revealed it.</summary>
    /// <param name="operatorSafeMessage">A message naming the owner's record and the operator's next step.</param>
    /// <param name="innerException">The provider failure this was raised for.</param>
    public OwnerSettingsUnwritableException(string operatorSafeMessage, Exception innerException)
        : base(operatorSafeMessage, innerException)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.OwnerSettingsUnwritable;
}
