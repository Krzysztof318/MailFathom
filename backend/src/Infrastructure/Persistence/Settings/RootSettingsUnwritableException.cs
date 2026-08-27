// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Infrastructure.Persistence.Settings;

/// <summary>Indicates that the database refused the statement a validated configuration write would have committed.</summary>
/// <remarks>
/// <para>
/// It is raised rather than returned because no caller between the statement and the administrator can decide what a
/// refused connection, a missing privilege, or a statement that outran its bound means. A writer that turned it into a
/// refusal beside "the value is invalid" and "somebody else won the race" would put a failure of the machinery in the
/// same list as three outcomes an operator's own edit produced.
/// </para>
/// <para>
/// The deployment's configuration is unchanged when this is raised, with two exceptions, and they are the reason the
/// rule is worth stating: the commit is one statement, so it either took effect or did not, and no reload token rises
/// for a statement that did not — but a statement that outran its command timeout, and one whose connection broke
/// while it was in flight, had both been accepted by the server before it stopped answering, so which of the two
/// happened is not known from here. The message says so where it applies, and the version now in force is what
/// settles it. A retry is safe in every case either way, because the version guard refuses one composed over a
/// version a first attempt already moved.
/// </para>
/// <para>
/// The message names neither the connection, the credential, nor any part of the document. What the database actually
/// said stays reachable as <see cref="Exception.InnerException" /> for an operator's log.
/// </para>
/// </remarks>
public sealed class RootSettingsUnwritableException : MailFathomException
{
    /// <summary>Initializes a new failure naming what could not be written, and the provider failure that revealed it.</summary>
    /// <param name="operatorSafeMessage">A message naming the persisted configuration and the operator's next step.</param>
    /// <param name="innerException">The provider failure this was raised for.</param>
    public RootSettingsUnwritableException(string operatorSafeMessage, Exception innerException)
        : base(operatorSafeMessage, innerException)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.RootSettingsUnwritable;
}
