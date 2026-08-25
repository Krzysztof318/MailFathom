// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MailFathom.Infrastructure.Persistence.Settings;

namespace MailFathom.Host.Configuration.RootSettings;

/// <summary>Re-reads the persisted configuration and republishes it to everything bound to it.</summary>
/// <remarks>
/// <para>
/// Reloading is a read followed by a publish, and either half can refuse. A document that cannot be read leaves the
/// version already in force exactly where it was; a document that reads but is one the layer will not publish — not a
/// configuration object, carrying a setting read before the layer existed, or carrying one another store owns — is
/// rejected by version, which is what
/// an operator needs to know: the number they wrote is the number that did not take. What never happens is a fall back
/// to the sources beneath this layer, because those never carried the persisted values and reverting to them would
/// quietly change settings the deployment had already adopted.
/// </para>
/// <para>
/// Nothing on the read path triggers this. A reload follows a write, so what calls it is the surface that commits one.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this service.")]
internal sealed partial class RootSettingsReloader(
    RootSettingsConfigurationProvider provider,
    IRootSettingsDocumentReader reader,
    ILogger<RootSettingsReloader> logger)
{
    /// <summary>Reads the persisted configuration again and publishes it when it is usable.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true" /> when a candidate was published, <see langword="false" /> when the version in force was kept.</returns>
    public async Task<bool> ReloadAsync(CancellationToken cancellationToken)
    {
        RootSettingsDocument candidate;

        try
        {
            candidate = await reader.ReadAsync(cancellationToken);
        }
        catch (RootSettingsUnreadableException exception)
        {
            this.LogPersistedConfigurationUnreadable(provider.Version, exception);

            return false;
        }

        try
        {
            provider.Apply(candidate);
        }
        // Four exception types for one outcome, which is a candidate the layer will not publish: the framework's
        // parser reports a document that is not an object of configuration keys as a FormatException and leaves a
        // JsonException — a document nested deeper than the reader's maximum, which jsonb stores happily — to
        // propagate as it came, and the layer itself refuses a document carrying a setting read before it existed or
        // one the storage catalog persists in a store of its own.
        catch (Exception exception)
            when (exception is FormatException
                or JsonException
                or BootstrapOnlySettingPersistedException
                or MisroutedSettingPersistedException)
        {
            this.LogCandidateRejected(candidate.Version, provider.Version, exception);

            return false;
        }

        this.LogVersionPublished(candidate.Version);

        return true;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Persisted configuration version {PublishedVersion} is in force.")]
    private partial void LogVersionPublished(long publishedVersion);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Persisted configuration version {RejectedVersion} was rejected and is not in force. Version {ActiveVersion} stays in force.")]
    private partial void LogCandidateRejected(long rejectedVersion, long activeVersion, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The persisted configuration could not be re-read. Version {ActiveVersion} stays in force.")]
    private partial void LogPersistedConfigurationUnreadable(long activeVersion, Exception exception);
}
