// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Settings;

namespace MailFathom.Host.Configuration.RootSettings;

/// <summary>Composes the configuration a deployment would read if a candidate document were the persisted layer.</summary>
/// <remarks>
/// <para>
/// A persisted setting is never judged alone, because alone it is not what the deployment reads. The layer sits below
/// User Secrets, environment variables, and command-line arguments, so a value written into it can be beaten by one an
/// operator injected; and it sits above the deployment's files, so a section it half fills is completed from them. A
/// candidate validated against the document by itself would therefore refuse settings the deployment supplies from
/// above and accept ones it never reaches.
/// </para>
/// <para>
/// The whole source list is rebuilt rather than the live providers reused, and that is the point rather than an
/// inefficiency. Every source builds a provider of its own, so composing a candidate reads the files again into
/// objects nobody else holds, and nothing a candidate does — loading, refusing, being discarded — touches what the
/// running process is bound to. A write is rare enough that reading a handful of files for it costs nothing worth
/// arranging around.
/// </para>
/// <para>
/// The one source that is not rebuilt is the persisted layer itself, which deliberately hands out the same provider
/// every time so a reload publishes into the object the configuration root reads. That is exactly the source being
/// replaced, and replacing it with a source over the candidate document is how the candidate takes its place at its
/// own precedence rather than at the end of the list.
/// </para>
/// </remarks>
internal sealed class CandidateConfigurationComposer(
    IConfigurationManager configuration,
    RootSettingsConfigurationSource layer)
{
    /// <summary>Composes the effective configuration a candidate document would produce.</summary>
    /// <param name="candidate">The candidate persisted configuration document.</param>
    /// <returns>The composed configuration, which the caller owns and disposes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the JSON configuration parser refuses the candidate document.</exception>
    /// <exception cref="System.Text.Json.JsonException">Thrown when the candidate document cannot be read as JSON at all.</exception>
    /// <exception cref="BootstrapOnlySettingPersistedException">Thrown when the candidate carries a setting read before the layer is composed.</exception>
    /// <exception cref="MisroutedSettingPersistedException">Thrown when the candidate carries a setting the storage catalog persists in a store of its own.</exception>
    public ConfigurationRoot Compose(RootSettingsDocument candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        // One builder for every source, which is what a builder's own Build does: a source resolving a default from the
        // builder it is handed must see the same one its neighbours did.
        var builder = new ConfigurationBuilder();
        var providers = configuration.Sources
            .Select(source => ReferenceEquals(source, layer) ? new RootSettingsConfigurationSource(candidate) : source)
            .Select(source => source.Build(builder))
            .ToList();

        try
        {
            // Named by the built type rather than by the interface, because what comes back is owned: a configuration
            // root holds a reload subscription on every provider it built, and the caller is what disposes both.
            //
            // Built from the provider list rather than through the builder so that this method owns the failure as
            // well as the result. The root's constructor loads each provider in turn with no try of its own, and the
            // candidate layer's provider is exactly the one that refuses — so a builder-built root would drop every
            // provider constructed before it, each file source among them holding a change-token registration on the
            // shared file provider, on precisely the paths a refused write takes.
            return new ConfigurationRoot(providers);
        }
        catch
        {
            foreach (var provider in providers.OfType<IDisposable>())
            {
                provider.Dispose();
            }

            throw;
        }
    }
}
