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
    /// <exception cref="RootSettingsUnreadableException">Thrown when the persisted document is not a JSON object of configuration keys.</exception>
    /// <remarks>
    /// <para>
    /// The layer is inserted at the same boundary the deployment-provisioned files were, and after them, which is what
    /// places it above every file and below User Secrets, environment variables, and command-line arguments.
    /// <see cref="OperatorOverrideBoundary" /> holds why that is the order.
    /// </para>
    /// <para>
    /// Inserting the source is what parses the document, because the configuration manager builds a provider as it is
    /// added. A document the parser refuses is therefore a startup failure here, and it is reported under the same
    /// code as a document that could not be read at all: an operator meets one condition — the persisted configuration
    /// did not become settings — and greps one number for it.
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
            configuration.Sources.Insert(OperatorOverrideBoundary.FindIn([.. configuration.Sources]), source);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new RootSettingsUnreadableException(
                $"The persisted configuration document at version {document.Version} is not a JSON object of configuration keys, so MailFathom composed no settings from it.",
                exception);
        }

        return source.Provider;
    }

    /// <summary>Reads the deployment's persisted configuration and layers it in.</summary>
    /// <param name="configuration">The host builder's configuration, which supplies the bootstrap settings and receives the layer.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The provider a reload publishes a later document through.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <exception cref="RootSettingsUnreadableException">Thrown when the persisted configuration cannot be read, or is not a JSON object of configuration keys.</exception>
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
