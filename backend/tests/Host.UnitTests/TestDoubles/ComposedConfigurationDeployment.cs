// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Configuration;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Administration;
using MailFathom.Host.Configuration.Provisioning;
using MailFathom.Host.Configuration.RootSettings;
using MailFathom.Infrastructure.Persistence.Settings;
using MailFathom.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>A deployment composed the way the host composes one, over sources a test states.</summary>
/// <remarks>
/// <para>
/// The sources are layered in the host's own order, and the operator's override arrives as the user-secrets file
/// because that is what the layer's insertion point recognizes as the lowest of the operator's sources. The persisted
/// layer therefore sits below it and above the deployment's own file, exactly as it does in a running process — which
/// is the whole of what a reading reports and what a write is refused by.
/// </para>
/// <para>
/// The other two overrides a real deployment uses — environment variables and command-line arguments — are composed on
/// request, above the layer where the host puts them. Both are the framework's own provider types rather than
/// substitutes, because what a reading answers about a source is decided by the type the composition built it as: a
/// double that composed a JSON file in their place would resolve every override assertion through the user-secrets arm
/// and leave the two arms that matter reached by nothing. The environment provider is the framework's with its data
/// stated rather than read, since the process environment is shared by every test in this assembly and a test that
/// wrote to it would not be safe to run beside another.
/// </para>
/// <para>
/// The caller is the deployment administrator, granted both permissions the configuration surface is published under,
/// because that is what every test about a reading or a write is arranging around. A test about the grant itself names
/// what it wants through <c>granted</c>, and states the refusal it expects.
/// </para>
/// <para>
/// The writer behind the administration is the real one rather than a substitute. What the administration adds —
/// refusing a write nothing will read, dropping a change that changes nothing, and reporting the effective value on
/// both sides of a commit — is only worth anything if the commit beneath it is judged by the deny-list, the route
/// catalog, the secret rule, the candidate binding, and the version guard a deployment actually applies.
/// </para>
/// </remarks>
internal sealed class ComposedConfigurationDeployment : IDisposable
{
    /// <summary>The instant the candidate validators read, fixed because nothing here is about the passage of time.</summary>
    private static readonly DateTimeOffset AnyInstant = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private const string DeploymentFileName = "10-deployment.json";
    private const string OperatorOverrideFileName = "secrets.json";

    private readonly ConfigurationManager configuration;

    private ComposedConfigurationDeployment(
        ConfigurationManager configuration,
        RootSettingsConfigurationSource layer,
        InMemoryRootSettingsRow row,
        AccessAuthorization authorization)
    {
        this.configuration = configuration;
        this.Row = row;
        this.Reader = new EffectiveSettingsReader(configuration, layer.Provider);

        var writer = new RootSettingsWriter(
            row,
            row,
            new CandidateConfigurationComposer(configuration, layer),
            new CandidateSettingsValidator(new FakeTimeProvider(AnyInstant), []),
            new RootSettingsReloader(layer.Provider, row, new RecordingLogger<RootSettingsReloader>()),
            DeclaredSecretScheme.Registered,
            new RecordingLogger<RootSettingsWriter>());

        this.Administration = new PersistedSettingsAdministration(authorization, this.Reader, row, writer);
    }

    /// <summary>Gets the reading side, which reports where each value the deployment composed came from.</summary>
    public EffectiveSettingsReader Reader { get; }

    /// <summary>Gets the service the configuration routes are served by.</summary>
    public PersistedSettingsAdministration Administration { get; }

    /// <summary>Gets the persisted row, which is what says whether a commit reached the database.</summary>
    public InMemoryRootSettingsRow Row { get; }

    /// <summary>Gets the name the deployment's provisioned file is composed under, which a reading reports as an origin.</summary>
    public static string ProvisionedFileName => DeploymentFileName;

    /// <summary>Composes the deployment's file, the persisted layer, and an optional operator override.</summary>
    /// <param name="provisioned">The deployment's own configuration file, as JSON.</param>
    /// <param name="persisted">The persisted configuration document, as JSON.</param>
    /// <param name="operatorOverride">An override composed above the persisted layer, as JSON, or nothing.</param>
    /// <param name="version">The version the persisted document stands at.</param>
    /// <param name="granted">What the entry that admitted the caller resolved to, defaulting to both permissions the configuration surface is published under.</param>
    /// <param name="environmentVariables">The variables an environment provider supplies above the layer, keyed as configuration paths, or nothing.</param>
    /// <param name="commandLineArguments">The arguments a command-line provider supplies above every other source, or nothing.</param>
    /// <returns>The composed deployment.</returns>
    public static ComposedConfigurationDeployment Composed(
        string provisioned,
        string persisted,
        string? operatorOverride = null,
        long version = 1,
        MailFathomPermission[]? granted = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        string[]? commandLineArguments = null)
    {
        var configuration = new ConfigurationManager();
        var files = new InMemoryConfigurationFileProvider().WithFile(DeploymentFileName, provisioned);

        // The provisioned source the host constructs rather than a plain JSON one, because that type is how a reading
        // tells the deployment's own file from the operator's User Secrets store — which the framework identifies by
        // the file name 'secrets.json' alone, a name a deployment is free to give its own ConfigMap key. Composing a
        // plain source here would leave every File assertion resolving through the fall-through arm of SourceOf, so
        // deleting the arm that makes the distinction would break nothing in the suite.
        configuration.Sources.Add(new ProvisionedJsonConfigurationSource
        {
            FileProvider = files,
            Path = DeploymentFileName,
            Optional = false,
            ReloadOnChange = false,
        });

        if (operatorOverride is not null)
        {
            files.WithFile(OperatorOverrideFileName, operatorOverride);
            configuration.AddJsonFile(files, OperatorOverrideFileName, optional: false, reloadOnChange: false);
        }

        configuration.AddRootSettings(new RootSettingsDocument(persisted, version));

        // Appended after the layer was inserted, which is where the host composes them: the insertion point recognizes
        // the user-secrets file above, so the layer is already below every override by the time these arrive.
        if (environmentVariables is not null)
        {
            configuration.Sources.Add(new StatedEnvironmentVariables(environmentVariables));
        }

        if (commandLineArguments is not null)
        {
            configuration.Sources.Add(new CommandLineConfigurationSource { Args = commandLineArguments });
        }

        return new ComposedConfigurationDeployment(
            configuration,
            configuration.Sources.OfType<RootSettingsConfigurationSource>().Last(),
            new InMemoryRootSettingsRow(persisted, version),
            AccessAuthorizations.ForAdministratorGranted(
                granted ?? [MailFathomPermission.AdminRead, MailFathomPermission.AdminConfigurationWrite]));
    }

    /// <summary>Applies keyed changes over the version the row currently stands at.</summary>
    /// <param name="edits">The changes to apply.</param>
    /// <returns>What the write did.</returns>
    public Task<SettingsWriteOutcome> ApplyAsync(params ConfigurationEdit[] edits) =>
        this.ApplyAsync(evenIfShadowed: false, edits);

    /// <summary>Applies keyed changes, stating whether a shadowed setting is meant.</summary>
    /// <param name="evenIfShadowed">Whether a write to a setting an outranking source supplies is meant deliberately.</param>
    /// <param name="edits">The changes to apply.</param>
    /// <returns>What the write did.</returns>
    public Task<SettingsWriteOutcome> ApplyAsync(bool evenIfShadowed, params ConfigurationEdit[] edits) =>
        this.Administration.ApplyAsync(edits, this.Row.Version, evenIfShadowed, TestContext.Current.CancellationToken);

    /// <summary>Opens the persisted document as an editing session opens it.</summary>
    /// <returns>The document and the version it was read at.</returns>
    public Task<PersistedSettingsDocument> ReadDocumentAsync() =>
        this.Administration.ReadDocumentAsync(TestContext.Current.CancellationToken);

    /// <summary>Saves an edited document back over the version it was opened at.</summary>
    /// <param name="document">The document as the editing session left it.</param>
    /// <param name="version">The version the buffer was opened over.</param>
    /// <param name="evenIfShadowed">Whether a write to a setting an outranking source supplies is meant deliberately.</param>
    /// <returns>What the write did.</returns>
    public Task<SettingsWriteOutcome> ApplyDocumentAsync(
        string document,
        long version,
        bool evenIfShadowed = false) =>
        this.Administration.ApplyDocumentAsync(
            document,
            version,
            evenIfShadowed,
            TestContext.Current.CancellationToken);

    /// <summary>Copies what the files supply beneath a path into the persisted layer.</summary>
    /// <param name="prefix">The colon-delimited path to adopt beneath.</param>
    /// <param name="evenIfShadowed">Whether a write to a setting an outranking source supplies is meant deliberately.</param>
    /// <returns>What the write did.</returns>
    public Task<SettingsWriteOutcome> AdoptAsync(string prefix, bool evenIfShadowed = false) =>
        this.Administration.AdoptAsync(
            prefix,
            this.Row.Version,
            evenIfShadowed,
            TestContext.Current.CancellationToken);

    /// <inheritdoc />
    public void Dispose() => this.configuration.Dispose();

    /// <summary>The framework's environment provider over variables a test states rather than the process's own.</summary>
    /// <remarks>
    /// The real provider type, because <c>EffectiveSettingsReader</c> decides which layer supplied a value from the type
    /// the composition built — a substitute would be classified as no source at all. What is replaced is only where the
    /// variables come from: reading the process environment would make every test in this assembly depend on what the
    /// others put there, and writing to it would make them unsafe to run in parallel.
    /// </remarks>
    private sealed class StatedEnvironmentVariables(IReadOnlyDictionary<string, string?> variables) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => new StatedProvider(variables);

        private sealed class StatedProvider : EnvironmentVariablesConfigurationProvider
        {
            internal StatedProvider(IReadOnlyDictionary<string, string?> variables)
            {
                foreach (var variable in variables)
                {
                    this.Data[variable.Key] = variable.Value;
                }
            }

            public override void Load()
            {
                // Stated once at construction. Loading would replace them with the process's own.
            }
        }
    }
}
