// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Settings;

namespace MailFathom.Host.Configuration.RootSettings;

/// <summary>The persisted configuration layer, as one source in the host's ordinary configuration order.</summary>
/// <remarks>
/// The document is read before the source is created rather than by the provider it builds. Reading it is asynchronous
/// — a secret reference has to be resolved to reach the database at all — and a provider's own load is not, so a
/// provider that read for itself would either block a thread on a credential source that can stall or hide the read
/// behind a synchronous wait with no token to cancel it.
/// </remarks>
internal sealed class RootSettingsConfigurationSource : IConfigurationSource
{
    /// <summary>Initializes the source with the document the host read while composing its configuration.</summary>
    /// <param name="document">The persisted configuration document and the version it was read at.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document" /> is <see langword="null" />.</exception>
    public RootSettingsConfigurationSource(RootSettingsDocument document) =>
        this.Provider = new RootSettingsConfigurationProvider(document);

    /// <summary>Gets the one provider this source ever builds, which is what a reload publishes through.</summary>
    /// <remarks>
    /// Created here rather than in <see cref="Build" /> so that the instance a reload holds is the instance the
    /// configuration root reads. A source that minted a provider per call would publish a reloaded document into an
    /// object nothing is bound to.
    /// </remarks>
    public RootSettingsConfigurationProvider Provider { get; }

    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder) => this.Provider;
}
