// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence;

/// <summary>Names which configured setting supplies the PostgreSQL password each time a physical connection opens.</summary>
/// <remarks>
/// The password is deliberately kept out of the connection string the data source is built from, so a credential
/// rotated behind an unchanged reference reaches every connection opened afterwards. A connection already open keeps
/// the credential it authenticated with, which is what makes rotation safe for work in flight.
/// </remarks>
public enum DatabasePasswordSource
{
    /// <summary>No rotatable source: the deployment authenticates without a password, or the connection string carries one that never passed through a secret block.</summary>
    None = 0,

    /// <summary>The <c>Persistence:Password</c> block supplies it.</summary>
    PasswordSecret = 1,

    /// <summary>The <c>Persistence:ConnectionString</c> block carries it inside the whole connection string.</summary>
    ConnectionStringSecret = 2,
}
