// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Host.Configuration.Persistence;
using MailFathom.Infrastructure.Persistence.Settings;
using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.Host.Configuration.RootSettings;

/// <summary>Layers the deployment's persisted configuration into the sources the host builder has already composed.</summary>
internal static class RootSettingsConfigurationExtensions
{
    /// <summary>Layers in the persisted configuration document, directly below the operator's overrides.</summary>
    /// <param name="configuration">The host builder's configuration, which is both the source list and where the layer is inserted.</param>
    /// <param name="document">The persisted configuration document and the version it was read at.</param>
    /// <returns>The provider a reload publishes a later document through.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> or <paramref name="document" /> is <see langword="null" />.</exception>
    /// <exception cref="RootSettingsUnreadableException">Thrown when the JSON configuration parser refuses the persisted document.</exception>
    /// <exception cref="BootstrapOnlySettingPersistedException">Thrown when the document carries a setting read before this layer is composed.</exception>
    /// <remarks>
    /// <para>
    /// The layer is inserted at the same boundary the deployment-provisioned files were, and after them, which is what
    /// places it above every file and below User Secrets, environment variables, and command-line arguments.
    /// <see cref="OperatorOverrideBoundary" /> holds why that is the order.
    /// </para>
    /// <para>
    /// The document is parsed here, before the source joins the list, and that ordering is the whole reason this
    /// method loads a provider by hand. Inserting into a <c>ConfigurationManager</c> rebuilds and reloads <em>every</em>
    /// source it holds, so a mounted file rewritten mid-rollout would refuse inside the insert with the same
    /// <see cref="FormatException" /> a bad persisted document raises — and reporting that as the persisted layer's
    /// failure would send an operator to the database for a broken file. Loading the provider on its own leaves this
    /// translation reading only its own document, and lets another source's refusal travel as itself.
    /// </para>
    /// </remarks>
    public static RootSettingsConfigurationProvider AddRootSettings(
        this IConfigurationManager configuration,
        RootSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(document);

        var source = new RootSettingsConfigurationSource(document);

        try
        {
            source.Provider.Load();
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            // The parser's own message is carried rather than summarized, because it names which refusal this was —
            // a root that is not an object, or the key two entries differing only in case collided on, which a jsonb
            // column preserves and a configuration dictionary cannot. It names no value.
            throw new RootSettingsUnreadableException(
                $"The JSON configuration parser refused the persisted configuration document at version {document.Version}, so MailFathom composed no settings from it: {exception.Message}",
                exception);
        }

        configuration.Sources.Insert(OperatorOverrideBoundary.FindIn([.. configuration.Sources]), source);

        return source.Provider;
    }

    /// <summary>Reads the deployment's persisted configuration and layers it in.</summary>
    /// <param name="configuration">The host builder's configuration, which supplies the bootstrap settings and receives the layer.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The provider a reload publishes a later document through.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <exception cref="RootSettingsUnreadableException">Thrown when the persisted configuration cannot be read, or the JSON configuration parser refuses it.</exception>
    /// <exception cref="BootstrapOnlySettingPersistedException">Thrown when the document carries a setting read before this layer is composed.</exception>
    /// <remarks>
    /// Everything the read needs — where the database is, which secret block carries its credential, and how a
    /// configured secret value is interpreted — is read from the sources beneath this layer and never from the layer
    /// itself. That is what makes the bootstrap settings a fixed set rather than a circular one: a persisted value for
    /// any of them could not be read without first reading it.
    /// </remarks>
    public static async Task<RootSettingsConfigurationProvider> AddRootSettingsAsync(
        this IConfigurationManager configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Bound as strictly as HostComposition binds the same section, and for the reason it states there: a
        // misspelled `Passwrod` would leave the secret block undiscovered and connect without the credential. That
        // mistake reaches this read before ValidateOnStart can ever fire, so binding loosely here would turn the
        // precise diagnosis into a database that appears to be unreachable.
        var persistenceSettings = configuration
            .GetSection(PersistenceOptions.SectionName)
            .Get<PersistenceOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            ?? new PersistenceOptions();

        var document = await RootSettingsBootstrap.ReadAsync(
            new DatabaseConnectionSettingsMapper(configuration).Map(persistenceSettings),
            configuration.GetValue("Secrets:Interpretation", SecretValueInterpretation.ReferenceOnly),
            cancellationToken);

        return configuration.AddRootSettings(document);
    }
}
