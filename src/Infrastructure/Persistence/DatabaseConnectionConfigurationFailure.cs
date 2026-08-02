// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence;

/// <summary>Identifies why reloaded database connection settings cannot be adopted.</summary>
/// <remarks>
/// The identity is the whole failure vocabulary a diagnostic may carry. Neither the reference target, nor the resolved
/// connection string, nor the credential inside it may accompany it.
/// </remarks>
public enum DatabaseConnectionConfigurationFailure
{
    /// <summary>The reference resolved, but the material behind it is not a valid PostgreSQL connection string.</summary>
    ConnectionStringNotParsable = 0,

    /// <summary>The connection string that supplies the credential no longer carries a password.</summary>
    ConnectionStringCarriesNoPassword = 1,

    /// <summary>Which setting supplies the credential changed, which the running connection pool cannot adopt.</summary>
    /// <remarks>Rotating a credential and re-provisioning where credentials come from are different acts; only the first is reloadable.</remarks>
    CredentialSourceChangeRequiresRestart = 2,
}
