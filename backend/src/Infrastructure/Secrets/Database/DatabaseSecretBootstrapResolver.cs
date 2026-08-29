// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.Infrastructure.Secrets.Database;

/// <summary>Refuses a database secret while the database connection itself is being composed.</summary>
internal sealed class DatabaseSecretBootstrapResolver : ISecretSchemeResolver
{
    /// <inheritdoc />
    public SecretReferenceScheme Scheme => DatabaseSecretReference.Scheme;

    /// <inheritdoc />
    public Task<SecretResolutionResult> ResolveAsync(
        SecretReference reference,
        CancellationToken cancellationToken) =>
        Task.FromResult(SecretResolutionResult.Failed(SecretResolutionFailure.BootstrapDependencyNotPermitted));
}
