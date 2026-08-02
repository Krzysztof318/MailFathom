// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Infrastructure.Secrets;

/// <summary>Names one secret retrieval mechanism a secret reference can select.</summary>
/// <remarks>
/// The scheme set is open: an adapter declares the scheme it serves and
/// <see cref="CompositeSecretReferenceResolver" /> builds its dispatch from the registered adapters. Adding a managed
/// store therefore registers one more <see cref="ISecretSchemeResolver" /> and edits no existing type. The value is a
/// reference type rather than a <see langword="readonly" /> <see langword="record" /> <see langword="struct" /> because
/// <c>default</c> would otherwise produce a scheme whose name is <see langword="null" />; it is parsed once per
/// resolution and never sits on a hot path.
/// </remarks>
public sealed record SecretReferenceScheme
{
    private SecretReferenceScheme(string name) => this.Name = name;

    /// <summary>Gets the scheme that reads the runtime credentials directory systemd exposes to the service.</summary>
    public static SecretReferenceScheme SystemdCredential { get; } = new("systemd-credential");

    /// <summary>Gets the scheme that reads a deployment-provisioned protected file, which also serves container and Kubernetes secret mounts.</summary>
    public static SecretReferenceScheme File { get; } = new("file");

    /// <summary>Gets the scheme that reads an environment variable, recommended only for non-production automation.</summary>
    /// <remarks>The platform hands the value over as a <see cref="string" />, which cannot be erased; see <see cref="ResolvedSecret" />.</remarks>
    public static SecretReferenceScheme EnvironmentVariable { get; } = new("env");

    /// <summary>Gets the scheme that names a literal value inline.</summary>
    public static SecretReferenceScheme Plaintext { get; } = new("plaintext");

    /// <summary>Gets the normalized lower-case wire name that appears before the first colon of a reference.</summary>
    public string Name { get; }

    /// <summary>Creates a scheme for an adapter declared outside this assembly.</summary>
    /// <param name="name">The wire name, matched ignoring case.</param>
    /// <returns>The scheme carrying the normalized name.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name" /> is <see langword="null" />, empty, or whitespace.</exception>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "The wire name is lower-case by specification and is never used as a security decision key; upper-casing it would print a scheme no configuration file spells that way.")]
    public static SecretReferenceScheme Create(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new SecretReferenceScheme(name.Trim().ToLowerInvariant());
    }

    /// <inheritdoc />
    public override string ToString() => this.Name;
}
