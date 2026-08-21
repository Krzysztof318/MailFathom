// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;

namespace MailFathom.Infrastructure.Secrets.References;

/// <summary>Resolves <c>systemd-credential:&lt;name&gt;</c> against the credentials directory systemd exposes to the service.</summary>
/// <remarks>
/// systemd derives the path to a unit's credentials from <c>$CREDENTIALS_DIRECTORY</c> and restricts access to the
/// service's own user, which is why the directory is read from the environment rather than configured. A target that
/// carries a path separator or a parent-directory segment is refused outright, so a reference cannot escape the
/// directory the unit was granted.
/// </remarks>
internal sealed class SystemdCredentialSecretReferenceResolver(
    IEnvironmentVariableReader environmentVariableReader,
    ISecretFileReader secretFileReader) : ISecretSchemeResolver
{
    private const string CredentialsDirectoryVariableName = "CREDENTIALS_DIRECTORY";

    /// <inheritdoc />
    public SecretReferenceScheme Scheme => SecretReferenceScheme.SystemdCredential;

    /// <inheritdoc />
    public Task<SecretResolutionResult> ResolveAsync(SecretReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var credentialsDirectory = environmentVariableReader.GetValue(CredentialsDirectoryVariableName);
        if (string.IsNullOrWhiteSpace(credentialsDirectory))
        {
            return Task.FromResult(SecretResolutionResult.Failed(SecretResolutionFailure.CredentialsDirectoryUnavailable));
        }

        var credentialName = reference.Target;
        if (!IsPlainCredentialName(credentialName))
        {
            return Task.FromResult(SecretResolutionResult.Failed(SecretResolutionFailure.TargetMissing));
        }

        return secretFileReader.ReadAsync(
            Path.Combine(credentialsDirectory, credentialName),
            SecretMaterialLimits.MaximumMaterialByteCount,
            cancellationToken);
    }

    private static bool IsPlainCredentialName(string credentialName) => credentialName.Length > 0
        && !credentialName.Contains('/', StringComparison.Ordinal)
        && !credentialName.Contains('\\', StringComparison.Ordinal)
        && !credentialName.Contains("..", StringComparison.Ordinal);
}
