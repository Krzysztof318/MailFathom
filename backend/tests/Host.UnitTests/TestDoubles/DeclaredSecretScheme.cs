// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>An adapter that declares a scheme this deployment serves and resolves nothing.</summary>
/// <remarks>
/// What a configuration write asks of the registered adapters is which schemes exist, never any material, so a double
/// that answers the first and refuses the second is the whole of the collaborator. Resolving throws rather than
/// returning an empty result, so a test that came to depend on retrieval fails where it did instead of reading a
/// silence as an answer.
/// </remarks>
internal sealed class DeclaredSecretScheme(SecretReferenceScheme scheme) : ISecretSchemeResolver
{
    /// <summary>Gets the resolvers a deployment registers by default, as a write sees them.</summary>
    public static IReadOnlyList<ISecretSchemeResolver> Registered { get; } =
    [
        new DeclaredSecretScheme(SecretReferenceScheme.SystemdCredential),
        new DeclaredSecretScheme(SecretReferenceScheme.File),
        new DeclaredSecretScheme(SecretReferenceScheme.EnvironmentVariable),
        new DeclaredSecretScheme(SecretReferenceScheme.Plaintext),
    ];

    /// <inheritdoc />
    public SecretReferenceScheme Scheme { get; } = scheme;

    /// <inheritdoc />
    public Task<SecretResolutionResult> ResolveAsync(SecretReference reference, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A configuration write asks which schemes are served and never resolves one.");
}
