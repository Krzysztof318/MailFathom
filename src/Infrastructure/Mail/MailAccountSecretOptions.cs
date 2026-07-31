// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Infrastructure.Secrets;

namespace MailMcp.Infrastructure.Mail;

/// <summary>Describes one unresolvable secret in an account's settings.</summary>
/// <param name="PropertyName">The setting the operator must correct.</param>
/// <param name="Failure">Why no material was produced. It is the whole permitted vocabulary: no target and no material accompanies it.</param>
public sealed record MailAccountSecretConfigurationError(string PropertyName, SecretResolutionFailure Failure);

/// <summary>Binds one account's secret-bearing settings and resolves them into owned material.</summary>
/// <remarks>
/// This is the configuration adapter for an account's secrets, alongside
/// <see cref="MailAccountTransportSecurityOptions" /> for its transport rules. It stays mutable and binder-friendly, and
/// the block shape means an operator's configuration file holds references rather than credentials.
/// </remarks>
public sealed class MailAccountSecretOptions
{
    /// <summary>Gets or sets the reference to the mailbox password or app password.</summary>
    public ConfiguredSecret Password { get; set; } = new();

    /// <summary>Resolves every secret and discards the material, reporting what an operator must fix.</summary>
    /// <param name="resolver">The resolver that turns references into material.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>One error per unresolvable secret, empty when the account's secrets are all reachable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolver" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Startup resolves and discards so an unreachable reference fails the host before any worker starts, while each
    /// actual use resolves again. Nothing is cached, so material rotated behind an unchanged reference is observed by
    /// the next operation with no cache to invalidate.
    /// </remarks>
    public async Task<IReadOnlyList<MailAccountSecretConfigurationError>> FindConfigurationErrorsAsync(
        ISecretReferenceResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var errors = new List<MailAccountSecretConfigurationError>();

        var passwordResult = await resolver.ResolveAsync(this.Password?.SecretReference, cancellationToken);
        if (passwordResult.Secret is { } password)
        {
            password.Dispose();
        }
        else
        {
            errors.Add(new MailAccountSecretConfigurationError(
                nameof(this.Password),
                passwordResult.Failure ?? SecretResolutionFailure.ReferenceMissing));
        }

        return errors;
    }

    /// <summary>Resolves the mailbox password and hands ownership of the material to the caller.</summary>
    /// <param name="resolver">The resolver that turns references into material.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>The owned password, which the caller must dispose when its operation ends.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolver" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a reference that passed startup validation no longer resolves.</exception>
    /// <remarks>The exception is a fail-closed path rather than an ordinary branch: startup already proved the reference resolves, so a failure here means the material disappeared underneath a running deployment.</remarks>
    public async Task<ResolvedSecret> ResolvePasswordAsync(
        ISecretReferenceResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var passwordResult = await resolver.ResolveAsync(this.Password?.SecretReference, cancellationToken);

        return passwordResult.Secret ?? throw new InvalidOperationException(
            $"The account password secret reference could not be resolved [{passwordResult.Failure}].");
    }
}
