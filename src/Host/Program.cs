// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI;
using MailFathom.AI.Chat;
using MailFathom.AI.Providers;
using MailFathom.Application.Accounts;
using MailFathom.Application.AiProviders;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Persistence;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.History;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Host;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Answering;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.DataEncryption;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Persistence;
using MailFathom.Host.Configuration.Providers;
using MailFathom.Host.Configuration.Provisioning;
using MailFathom.Host.Configuration.Rules;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Hosting;
using MailFathom.Host.Hosting.Startup;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.Observability;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Mcp;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Resilience;
using MailFathom.Infrastructure.Rules;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Mcp;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

// Composed before anything else, CreateBuilder included, so that a malformed appsettings.json, a failure during
// composition, and a failed host start are all reported rather than only printed. The pipeline the container owns
// does not exist until Build has returned, and on a startup failure it never flushes.
using var bootstrapLogger = BootstrapLogger.CreateFromEnvironment();
bootstrapLogger.RecordHostStarting();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Before anything reads configuration, so a mounted ConfigMap is ordinary configuration to every binding below
    // rather than a second source consulted afterwards. The files land beneath the environment-variable provider, which
    // keeps an environment variable an override of a mounted file rather than something a stale mount can beat.
    var provisionedConfigurationFileCount = builder.Configuration.AddProvisionedConfiguration();
    bootstrapLogger.RecordProvisionedConfigurationFiles(provisionedConfigurationFileCount);

    // Once every source exists and before anything binds one, because this asks whether a value can reach its reader at
    // all rather than what the value says. The few settings only the environment can deliver were read before this line
    // — by the pipeline above, by the OpenTelemetry exporter, by the .NET host, by OpenSSL — so a value that arrived
    // from anywhere else is refused here instead of being accepted and ignored.
    EnvironmentOnlySettings.RejectMisplacedValues(builder.Configuration, Environment.GetEnvironmentVariable);

    builder.AddServiceDefaults();
    builder.Services.AddProblemDetails();
    builder.Services.AddSingleton(TimeProvider.System);
    // ReferenceOnly is the default, so a deployment that configures nothing gets the mode under which a plain-text value
    // where a reference belongs fails startup instead of authenticating.
    builder.Services.AddSecretResolution(
        builder.Configuration.GetValue("Secrets:Interpretation", SecretValueInterpretation.ReferenceOnly));
    // The non-HTTP dependency classes only. HttpClient traffic, which is how the AI provider clients reach a hosted
    // model, is already wrapped once by AddStandardResilienceHandler in the service defaults above.
    builder.Services.AddOutboundResiliencePipelines(builder.Configuration.GetSection("Resilience"));
    // Bound strictly: mail transport is security-sensitive, and a misspelled key such as a singular
    // "PermittedAuthenticationMechanism" would otherwise be ignored and silently replaced by the default allow-list.
    builder.Services.AddOptions<MailSynchronizationOptions>()
        .Bind(
            builder.Configuration.GetSection("MailSynchronization"),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    // The one mail synchronization rule that needs the current date, which no attribute on a bound options graph can
    // reach, arrives through the options framework's own validator seam rather than as a second validation mechanism.
    builder.Services.AddSingleton<IValidateOptions<MailSynchronizationOptions>, MailSynchronizationWindowValidator>();
    // The host stops awaiting StopAsync once its own shutdown budget expires, so a drain configured beyond that budget
    // would be accepted and never honored: the process would exit with the work still running. The budget is therefore
    // derived from the configured drain instead of being left on the framework default. Read from configuration
    // directly for the same reason the text search configuration below is — the value has to be known while the host
    // is being built, before a container that could resolve an options snapshot exists. It is restart-required, which
    // is what a shutdown budget is by nature.
    builder.Services.Configure<HostOptions>(hostOptions => hostOptions.ShutdownTimeout =
        MailSynchronizationOptions.ResolveHostShutdownBudget(builder.Configuration.GetValue(
            "MailSynchronization:ShutdownDrainTimeout",
            new MailSynchronizationOptions().ShutdownDrainTimeout)));
    // Bound strictly for the same reason as mail transport: a misspelled "Passwrod" would leave the secret block
    // undiscovered, start the host on a passwordless connection string, and surface as an authentication failure later.
    builder.Services.AddOptions<PersistenceOptions>()
        .Bind(
            builder.Configuration.GetSection("Persistence"),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    // Bound strictly like the blocks above: a misspelled "SnippetsPerEmails" would leave the configured bound
    // undiscovered and search would quietly keep showing the default amount of every matched message.
    builder.Services.AddOptions<MailboxSearchOptions>()
        .Bind(
            builder.Configuration.GetSection("MailboxSearch"),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddOptions<MailExtractionBackfillOptions>()
        .Bind(
            builder.Configuration.GetSection("MailExtractionBackfill"),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddOptions<EmailContentOptions>()
        .Bind(
            builder.Configuration.GetSection("EmailContent"),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    // A configuration root of its own rather than a block inside the feature that first needed it. The address clients
    // reach this deployment at is a property of the installation, so an operator answers it once and whatever else has
    // to hand back an absolute address later reads the same key instead of adding a second one beside it.
    builder.Services.AddOptions<DeploymentOptions>()
        .Bind(
            builder.Configuration.GetSection(DeploymentOptions.SectionName),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    // A configuration root of its own, because what a deployment embeds with is a property of that deployment rather
    // than of its database or its mail accounts, and a root is also what gives its keys their own secret-name
    // uniqueness scope. An absent section is a deployment that embeds nothing and serves lexical search, which
    // ADR 0006 makes a supported state rather than a startup failure.
    builder.Services.AddOptions<EmbeddingOptions>()
        .Bind(
            builder.Configuration.GetSection("Embeddings"),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    // A configuration root of its own beside Embeddings rather than a section inside it, because the two are separate
    // choices with separate consequences: an instance may generate and not embed, or embed and not generate, and each
    // is a working deployment with a different set of capabilities. An absent section is one of those states rather
    // than a startup failure.
    // Bound without ValidateDataAnnotations, unlike every section around it, because this one reloads and the framework
    // validator is the wrong place for a reloadable group's rules: it runs while the options monitor materializes a
    // reloaded value, on the thread the configuration provider reported the change from, where a failure has nowhere to
    // be reported and the candidate is dropped in silence. ChatDeclarationRules runs the same validator from the two
    // places that can report it — composition below, which fails startup with every problem at once, and the reload
    // validator, which logs the refusal and leaves the previous declaration serving.
    builder.Services.AddOptions<ChatModelOptions>()
        .Bind(
            builder.Configuration.GetSection(ChatModelOptions.SectionName),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true);
    // A configuration root of its own beside Chat rather than a block inside it, because the two answer different
    // questions: Chat says which endpoint generates text and what one call to it may carry, while this says what
    // answering a question is allowed to cost and how much of a mailbox may leave the process to do it. Unlike the
    // provider sections, an absent section is not an absent capability — every deployment has these ceilings, and
    // writing nothing takes the conservative defaults rather than none.
    builder.Services.AddOptions<MailAnsweringOptions>()
        .Bind(
            builder.Configuration.GetSection(MailAnsweringOptions.SectionName),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    // Beside the declaration rather than inside it: what an instance embeds with is a commitment, and how fast it works
    // through the mail it already had is a rate an operator changes while watching a provider bill.
    builder.Services.AddOptions<EmbeddingBackfillOptions>()
        .Bind(
            builder.Configuration.GetSection("EmbeddingBackfill"),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    // A configuration root of its own rather than a section of Persistence: the database is the first thing sealed
    // under the ring and there is no reason it is the last, and a root is also what gives the key material its own
    // secret-name uniqueness scope. ADR 0005 records the whole decision.
    builder.Services.AddOptions<DataEncryptionOptions>()
        .Bind(
            builder.Configuration.GetSection("DataEncryption"),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    // A configuration root of its own, because what a deployment scans mail for before copying it or handing it out is
    // a property of that deployment rather than of its database, its accounts, or its providers, and the two switches
    // it holds reach several of those at once. Both are off by default, and an absent section is that default rather
    // than a startup failure.
    builder.Services.AddOptions<SensitiveContentOptions>()
        .Bind(
            builder.Configuration.GetSection(SensitiveContentOptions.SectionName),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    // The category and rule names an operator wrote are judged against what the registered scanners declare, which no
    // attribute on the bound graph can reach. Registered whatever the switches say, because a switch turned on with no
    // detector behind it is exactly what it refuses.
    builder.Services.AddSingleton<IValidateOptions<SensitiveContentOptions>, SensitiveContentCatalogValidator>();

    // Read while the services are being registered, for the reason the provider declarations below are: whether this
    // deployment scans anything decides which services exist at all, and that decision is taken before a container that
    // could resolve an options snapshot. With both switches off nothing here is registered, so an opt-in nobody took
    // constructs no detector, holds no concurrency permit, and appears on no path.
    var declaredSensitiveContent = builder.Configuration
        .GetSection(SensitiveContentOptions.SectionName)
        .Get<SensitiveContentOptions>() ?? new SensitiveContentOptions();

    if (declaredSensitiveContent.Secrets.Enabled)
    {
        builder.Services.AddSecretContentScanning();
    }

    if (declaredSensitiveContent.IsAnyScannerEnabled)
    {
        builder.Services.AddSingleton(provider => SensitiveContentPlanMapper.Map(
            provider.GetRequiredService<IOptions<SensitiveContentOptions>>().Value,
            provider.GetServices<ISensitiveContentCatalog>())
            ?? throw new InvalidOperationException(
                "A sensitive-content scanner was switched on at registration and is absent from the validated configuration."));
        // A singleton because the concurrency bound it holds is one for the process, and because a plan is composed
        // once. Every consumer redacts through this one instance, which is what keeps the derived path and the read
        // path from drifting into two redactions of the same message.
        builder.Services.AddSingleton<SensitiveContentRedactor>();
    }

    // Rules are authored in configuration rather than in a table, which ADR 0010 records: what an instance will do to a
    // mailbox is then reviewable in a diff before it runs and reproducible from a repository afterwards. Bound strictly
    // for the reason mail transport is — a misspelled key would otherwise be ignored, and the rule it belonged to would
    // go on running while meaning something its author did not write.
    builder.Services.AddOptions<MailRulesOptions>()
        .Bind(
            builder.Configuration.GetSection(MailRulesOptions.SectionName),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    // Every condition is read here, while the host is being composed, because the condition language has no static type
    // checker of its own: an unknown fact or a comparison that could never hold would otherwise be discovered on real
    // mail. A rule set that cannot be read is a startup failure, and one compiler serves composition, every reload, and
    // every pass, because it holds no state.
    var mailRuleConditionCompiler = new NCalcMailRuleConditionCompiler();
    var declaredMailRules = builder.Configuration
        .GetSection(MailRulesOptions.SectionName)
        .Get<MailRulesOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true);
    var mailRuleDeclarationErrors = MailRuleDeclarationRules.FindDeclarationErrors(
        declaredMailRules,
        mailRuleConditionCompiler,
        DeclaredMailAccounts.ReadFrom(builder.Configuration));

    if (mailRuleDeclarationErrors.Count > 0)
    {
        throw new OptionsValidationException(
            MailRulesOptions.SectionName,
            typeof(MailRulesOptions),
            mailRuleDeclarationErrors);
    }

    builder.Services.AddSingleton<IMailRuleConditionCompiler>(mailRuleConditionCompiler);
    builder.Services.AddSingleton<MailRuleSetEvaluator>();
    builder.Services.AddSingleton<IMailRuleSetSource, ConfiguredMailRuleSetSource>();
    // The rule section validates itself on reload instead of letting the options framework drop an invalid candidate in
    // silence. That default is the wrong behavior here above everywhere else: an owner who mistypes a fact name would
    // get an instance that goes on acting on mail under the previous rules while their file says otherwise. A refused
    // candidate is logged and the last proven rule set stays in effect.
    // The accounts a scope may name are read from the published synchronization snapshot rather than captured here, so
    // an account added at run time is one a rule can be scoped to without restarting the process.
    builder.Services.AddSingleton(provider => new ValidatedSettingsSnapshot<MailRulesOptions>(
        provider.GetRequiredService<IOptionsMonitor<MailRulesOptions>>(),
        (candidate, _) => Task.FromResult(
            MailRuleDeclarationRules.FindDeclarationErrors(
                candidate,
                mailRuleConditionCompiler,
                DeclaredMailAccounts.ReadFrom(
                    provider.GetRequiredService<ISettingsSnapshot<MailSynchronizationOptions>>().Current))),
        MailRulesOptions.SectionName,
        provider.GetRequiredService<ILogger<ValidatedSettingsSnapshot<MailRulesOptions>>>()));
    builder.Services.AddSingleton<ISettingsSnapshot<MailRulesOptions>>(
        provider => provider.GetRequiredService<ValidatedSettingsSnapshot<MailRulesOptions>>());
    // Evaluation is a step of the account's synchronization run, so its collaborators are scoped exactly as that run's
    // other steps are: one scope per work unit, and the pass bounds read from the published snapshot when the scope is
    // built rather than captured once at startup.
    builder.Services.AddScoped(provider =>
        provider.GetRequiredService<ISettingsSnapshot<MailRulesOptions>>().Current.ToEvaluationOptions());
    // Scoped beside the pass rather than transient, because it remembers the folder a destination alias resolved to for
    // the length of the pass; a batch of mail matching one filing rule would otherwise re-read one binding per message.
    builder.Services.AddScoped<MailRuleActionRecorder>();
    builder.Services.AddScoped<MailRuleEvaluationPass>();
    builder.Services.AddScoped<MailRuleEvaluationRunRequests>();
    // The history's retention reads the same published snapshot the pass's bounds do, so shortening the window reaches
    // the next account run rather than the next restart.
    builder.Services.AddScoped<MailRuleHistoryRetention>();
    builder.Services.AddHostedService(
        provider => provider.GetRequiredService<ValidatedSettingsSnapshot<MailRulesOptions>>());
    // The published snapshot, not the bound one, is what every consumer reads: a reload whose secret references do not
    // resolve is rejected and leaves the previous configuration active for new operations.
    builder.Services.AddSingleton<DatabaseConnectionSettingsMapper>();
    builder.Services.AddSingleton<SecretConfigurationValidator>();
    builder.Services.AddSingleton(provider => new ValidatedSettingsSnapshot<MailSynchronizationOptions>(
        provider.GetRequiredService<IOptionsMonitor<MailSynchronizationOptions>>(),
        (candidate, cancellationToken) => provider.GetRequiredService<SecretConfigurationValidator>()
            .FindMailConfigurationErrorsAsync(candidate, cancellationToken),
        "MailSynchronization",
        provider.GetRequiredService<ILogger<ValidatedSettingsSnapshot<MailSynchronizationOptions>>>()));
    builder.Services.AddSingleton(provider => new ValidatedSettingsSnapshot<PersistenceOptions>(
        provider.GetRequiredService<IOptionsMonitor<PersistenceOptions>>(),
        (candidate, cancellationToken) => provider.GetRequiredService<SecretConfigurationValidator>()
            .FindPersistenceConfigurationErrorsAsync(candidate, cancellationToken),
        "Persistence",
        provider.GetRequiredService<ILogger<ValidatedSettingsSnapshot<PersistenceOptions>>>()));
    builder.Services.AddSingleton(provider => new ValidatedSettingsSnapshot<DataEncryptionOptions>(
        provider.GetRequiredService<IOptionsMonitor<DataEncryptionOptions>>(),
        (candidate, cancellationToken) => provider.GetRequiredService<SecretConfigurationValidator>()
            .FindDataEncryptionConfigurationErrorsAsync(candidate, cancellationToken),
        "DataEncryption",
        provider.GetRequiredService<ILogger<ValidatedSettingsSnapshot<DataEncryptionOptions>>>()));
    builder.Services.AddSingleton<ISettingsSnapshot<MailSynchronizationOptions>>(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<MailSynchronizationOptions>>());
    builder.Services.AddSingleton<ISettingsSnapshot<PersistenceOptions>>(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<PersistenceOptions>>());
    builder.Services.AddSingleton<ISettingsSnapshot<DataEncryptionOptions>>(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<DataEncryptionOptions>>());
    // The key ring reads the published snapshot on every operation, so a key an operator adds reaches the next seal or
    // open without a restart — which is the half of a rotation that must not need one.
    builder.Services.AddDataEncryption(provider => DataEncryptionKeyRingMapper.Map(
        provider.GetRequiredService<ISettingsSnapshot<DataEncryptionOptions>>().Current));
    // One work unit runs against one snapshot: the enclosing run hands its own down, and a scope with no enclosing run
    // falls back to the published one. That is what keeps the transport security policy a work unit validates against,
    // the material it connects with, and the account list it was scheduled from all from the same reload.
    builder.Services.AddScoped<ScopedMailSynchronizationSettings>();
    builder.Services.AddScoped(provider => provider.GetRequiredService<ScopedMailSynchronizationSettings>().Current);
    builder.Services.AddScoped<IMailTransportSecurityPolicyReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>());
    builder.Services.AddScoped<IMailSynchronizationWindowReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>());
    builder.Services.AddScoped<IRemotelyDeletedEmailDispositionReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>());
    builder.Services.AddScoped<IAuthoredDeleteEmailDispositionReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>());
    builder.Services.AddScoped<IMailRuleActionPermissionReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>());
    builder.Services.AddScoped<IMailboxMutationAuditSettingsReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>());
    builder.Services.AddScoped<IMailAnsweringAuditSettingsReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>());
    builder.Services.AddScoped<IMailAccountCatalog>(provider => provider.GetRequiredService<MailSynchronizationOptions>());
    builder.Services.AddScoped<IMailFolderParticipationReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>());
    builder.Services.AddScoped<IImapAccountSettingsProvider, ConfiguredImapAccountSettingsProvider>();
    builder.Services.AddScoped<IMailOAuthSettingsProvider, ConfiguredMailOAuthSettingsProvider>();
    // A singleton, unlike the settings around it, because the pool that reads it is one: the write connection is
    // bounded per account across the process rather than per work unit. The idle period is therefore read once at
    // startup, which is why the configuration reference marks it as needing a restart.
    builder.Services.AddSingleton(provider => new MailboxWriteSessionOptions
    {
        ConnectionIdlePeriod = provider.GetRequiredService<ISettingsSnapshot<MailSynchronizationOptions>>()
            .Current.WriteConnectionIdlePeriod,
    });
    // A singleton for the same reason: the bound is one answer for the process, and a mutation's attempts are counted
    // across runs rather than within one, so reading it per scope would let two runs of the same change disagree about
    // when it has had enough.
    builder.Services.AddSingleton(provider => new MailboxMutationOptions
    {
        MaximumAttempts = provider.GetRequiredService<ISettingsSnapshot<MailSynchronizationOptions>>()
            .Current.MaxMutationAttempts,
    });
    builder.Services.AddScoped(provider =>
    {
        var synchronizationSettings = provider.GetRequiredService<MailSynchronizationOptions>();
        return new MailboxConvergenceOptions
        {
            MaxMutationsPerPass = synchronizationSettings.MaxMutationsPerConvergencePass,
            UnknownOutcomeGrace = synchronizationSettings.UnknownMutationOutcomeGrace,
        };
    });
    builder.Services.AddScoped(provider =>
    {
        var synchronizationSettings = provider.GetRequiredService<MailSynchronizationOptions>();
        return new MailboxSynchronizationOptions
        {
            MaxMetadataBatchSize = synchronizationSettings.MaxMetadataBatchSize,
            MaxRawMimeBytes = synchronizationSettings.MaxRawMimeBytes,
            MaxMetadataBatchesPerRun = synchronizationSettings.MaxMetadataBatchesPerRun,
            MaxReconciledEmailsPerRun = synchronizationSettings.MaxReconciledEmailsPerRun,
            MaxContentBytesPerRun = synchronizationSettings.MaxContentBytesPerRun,
        };
    });
    // A singleton, because the ceiling is one answer for the one content store every account writes into. Reading it
    // per scope would give each concurrent folder run a ceiling of its own, which is the sum it exists to bound.
    builder.Services.AddSingleton(provider => new StoredContentCeiling(
        provider.GetRequiredService<ISettingsSnapshot<MailSynchronizationOptions>>().Current.MaxStoredContentBytes));
    // A singleton, because what it bounds is the memory of the whole process rather than of any one run: a budget read
    // per scope would give every concurrent work unit a budget of its own, which is the sum this exists to bound. That
    // is also why the capacity is read once at startup, which the configuration reference marks as needing a restart.
    builder.Services.AddSingleton(provider => new RawMimeMemoryBudget(
        provider.GetRequiredService<ISettingsSnapshot<MailSynchronizationOptions>>().Current.MaxInFlightRawMimeBytes));
    builder.Services.AddScoped(provider =>
    {
        var synchronizationSettings = provider.GetRequiredService<MailSynchronizationOptions>();
        return new EmailMimeExtractionOptions
        {
            MaxPartCount = synchronizationSettings.MaxMimePartCount,
            MaxNestingDepth = synchronizationSettings.MaxMimeNestingDepth,
            MaxExtractedTextCharacters = synchronizationSettings.MaxExtractedTextCharacters,
        };
    });
    builder.Services.AddScoped(provider =>
    {
        var contentSettings = provider.GetRequiredService<IOptions<EmailContentOptions>>().Value;
        return new EmailContentReadOptions
        {
            MaxBodyCharacters = contentSettings.MaxBodyCharacters,
            MaxCharactersPerRead = contentSettings.MaxCharactersPerRead,
        };
    });
    // A singleton beside the scoped read bounds above, because what it carries is where this deployment publishes itself
    // and how long a capability it hands out lives — two facts about the process rather than about a request. The two
    // come from different sections deliberately: the address is a property of the installation that anything handing
    // back an absolute URL will want, and the lifetime belongs to the capability this one feature issues. The route is
    // composed onto the address here, so the address a link points at and the route this host maps below are one
    // decision written once.
    builder.Services.AddSingleton(provider => new AttachmentDownloadSettings(
        provider.GetRequiredService<IOptions<DeploymentOptions>>().Value
            .ComposeAddressFor(EmailAttachmentDownloadEndpoint.RoutePrefix),
        provider.GetRequiredService<IOptions<EmailContentOptions>>().Value.AttachmentDownloads.LinkLifetime));
    builder.Services.AddScoped(provider =>
    {
        var backfillSettings = provider.GetRequiredService<IOptions<MailExtractionBackfillOptions>>().Value;
        return new StoredEmailExtractionBackfillOptions
        {
            BatchSize = backfillSettings.BatchSize,
            MaxBatchesPerRun = backfillSettings.MaxBatchesPerRun,
        };
    });
    builder.Services.AddScoped(provider =>
    {
        var embeddingBackfillSettings = provider.GetRequiredService<IOptions<EmbeddingBackfillOptions>>().Value;
        return new StoredEmailEmbeddingBackfillOptions
        {
            BatchSize = embeddingBackfillSettings.BatchSize,
            MaxBatchesPerRun = embeddingBackfillSettings.MaxBatchesPerRun,
        };
    });
    builder.Services.AddSingleton(provider => new PersistenceConcurrencyOptions
    {
        MaximumCommitAttempts = provider.GetRequiredService<IOptions<PersistenceOptions>>().Value.MaximumConcurrencyCommitAttempts,
    });
    // A singleton rather than a scoped value: the bound is a deployment-wide privacy control, so every search in the
    // process applies the one an operator configured rather than whichever snapshot a scope happened to open under.
    builder.Services.AddSingleton(provider =>
    {
        var searchSettings = provider.GetRequiredService<IOptions<MailboxSearchOptions>>().Value;
        return EmailSearchSnippetBounds.Create(searchSettings.SnippetsPerEmail, searchSettings.WordsPerSnippet);
    });
    // What the startup probe reports. Both gates reach a remote dependency, so both take as long as that dependency
    // does, and an orchestrator's startup probe is what turns that interval into an extended grace period rather than
    // into a failing instance. The probe answers from this tracker rather than from the order the framework happens to
    // start its hosted services in.
    builder.Services.AddSingleton(new HostStartupGates(
        HostStartupGate.SecretConfiguration,
        HostStartupGate.DatabaseSchema));
    builder.Services.AddHealthChecks()
        .AddCheck<HostStartupGatesHealthCheck>(HostStartupGatesHealthCheck.Name, tags: [HealthProbe.Startup.Tag]);
    // The validator is registered ahead of the worker so hosted-service ordering reinforces the StartingAsync ordering
    // rather than depending on it alone, and ahead of the infrastructure so an operator who mistyped several references
    // reads one aggregated report rather than whichever failure the database happened to hit first.
    builder.Services.AddHostedService<SecretConfigurationStartupValidator>();
    // Registered after the startup gate so the first snapshot is proven before either begins accepting reloaded ones.
    builder.Services.AddHostedService(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<MailSynchronizationOptions>>());
    builder.Services.AddHostedService(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<PersistenceOptions>>());
    builder.Services.AddHostedService(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<DataEncryptionOptions>>());
    // The secret blocks come from the published snapshot on every read, so a reference an operator repoints reaches the
    // next physical connection instead of waiting for a restart.
    // The text search configuration is taken once, from configuration directly, because the EF Core model has to be
    // described before the container that would resolve an options snapshot exists — and because the value is compiled
    // into the schema, so adopting a reloaded one would leave the index describing the configuration it replaced. An
    // unsupported name throws here and is recorded by the bootstrap logger; PersistenceOptions validates the same value
    // on start, which is what reports the supported alternatives to an operator.
    var configuredTextSearchConfiguration = builder.Configuration["Persistence:TextSearchConfiguration"];

    // Before persistence, which writes what these derive: the chunk writer resolves the chunker from here.
    builder.Services.AddLocalTextDerivations();

    // Read here rather than resolved, for the reason the text search configuration below is: whether this deployment
    // embeds at all decides which services exist, and that decision is taken before the container that would resolve
    // an options snapshot. Only the presence of a chain is read this way — every rule about what the chain declares is
    // validated on start, where a failure reports every problem at once instead of the first one to be built.
    var declaredEmbeddings = builder.Configuration.GetSection("Embeddings").Get<EmbeddingOptions>();

    // Registered whichever way that reads, because the synchronization run offers every committed message into the
    // backlog and does not ask whether anything is embedding: an instance with no provider simply holds a backlog nobody
    // drains, which costs one bounded set of identifiers and keeps the condition in one place.
    builder.Services.AddSingleton(provider => new EmailEmbeddingBacklogOptions
    {
        Capacity = provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value.MaxQueuedEmails,
    });

    // What this deployment declares it embeds with, registered whichever way the reading above went. The administrative
    // surface needs the answer on an instance that declared nothing at all — that is the instance whose operator is
    // asking why semantic search is not working — so the absence is a value here rather than a missing registration.
    builder.Services.AddSingleton(provider => new DeclaredEmbeddingGeometry(
        EmbeddingGenerationPlanMapper.Map(provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value)?.Identity));

    // The three ceilings, registered the same way and for the same reason the backlog bound is: each of them applies to
    // an instance rather than to an activation. Passages are cut for every synchronized message whether or not a
    // provider was ever declared, and what a period has spent is a figure an operator reads before deciding to declare
    // one — so registering these behind the declaration would leave the chunker without a bound and the ledger without
    // a budget to be read against.
    builder.Services.AddSingleton(provider => EmbeddingInputBound.Create(
        provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value.MaxCharactersPerEmail));
    builder.Services.AddSingleton(provider =>
    {
        var settings = provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value;

        return EmbeddingSpendBudget.Create(settings.MaxInputCharactersPerPeriod, settings.SpendPeriod);
    });
    // A singleton because the reservation it hands out is what makes one process's requests add up to the declared
    // rate; one per scope would let every worker send at the full rate on its own.
    builder.Services.AddSingleton(provider => EmbeddingRequestPacer.Create(
        provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value.MaxRequestsPerMinute,
        provider.GetRequiredService<TimeProvider>()));

    // Read the same way and for the same reason as the embedding declaration: whether this deployment generates text
    // decides which services exist, and that decision is taken before the container that would resolve an options
    // snapshot. Unlike the embedding chain, everything else this says is reloadable, so what is frozen here is the
    // presence of the declaration and nothing more — the model, the address, the parameters, and the bounds are read
    // again from the published snapshot on every question.
    var declaredChat = builder.Configuration.GetSection(ChatModelOptions.SectionName).Get<ChatModelOptions>();

    // Read the same way, and needed before the container for the same reason: the retrieval ceiling it carries is what
    // caps the relevance filter's candidate count and what supplies that count's default, and both are decided while
    // the services are being registered. An absent section binds to the defaults rather than to nothing, so this never
    // has to be treated as optional the way a provider declaration is.
    var declaredAnswering = builder.Configuration.GetSection(MailAnsweringOptions.SectionName).Get<MailAnsweringOptions>()
        ?? new MailAnsweringOptions();

    // Validated here rather than through ValidateOnStart, for the reason the endpoint sections below are: the mapping
    // on the next line happens while the builder is being composed, so the container that would have run the pipeline
    // does not exist yet. Without this a typo would first be noticed by an ArgumentOutOfRangeException out of a Create
    // method and reach an operator as a framework stack trace instead of the aggregated report every other section
    // produces.
    var answeringConfigurationErrors = declaredAnswering.FindConfigurationErrors();

    if (answeringConfigurationErrors.Count > 0)
    {
        throw new OptionsValidationException(
            MailAnsweringOptions.SectionName,
            typeof(MailAnsweringOptions),
            answeringConfigurationErrors);
    }

    var answeringBudget = MailAnsweringBudgetMapper.Map(declaredAnswering);

    // Every rule the chat declaration answers to, in one reading: the section's own bounds, the alias that names one AI
    // endpoint across the whole deployment because a credential, a resilience circuit, and a log line are all keyed by
    // it, and the filter's candidate count against what a lookup actually hands over. Judged here rather than through
    // ValidateOnStart so the same rules judge a reloaded candidate, and reported together so an operator who wrote two
    // mistakes reads both.
    var chatConfigurationErrors = ChatDeclarationRules.FindDeclarationErrors(
        declaredChat,
        declaredEmbeddings,
        declaredAnswering);

    if (chatConfigurationErrors.Count > 0)
    {
        throw new OptionsValidationException(
            ChatModelOptions.SectionName,
            typeof(ChatModelOptions),
            chatConfigurationErrors);
    }

    // Registered whether or not a chat endpoint was declared, because the credential source below reads it on behalf of
    // an embedding-only deployment too, and because an instance that declared nothing is the one whose operator has to
    // be told that adding the section takes a restart rather than being ignored.
    builder.Services.AddSingleton(provider => new ChatSettingsReloadValidator(
        provider.GetRequiredService<SecretConfigurationValidator>(),
        declaredChat ?? new ChatModelOptions(),
        declaredEmbeddings,
        declaredAnswering));
    builder.Services.AddSingleton(provider => new ValidatedSettingsSnapshot<ChatModelOptions>(
        provider.GetRequiredService<IOptionsMonitor<ChatModelOptions>>(),
        (candidate, cancellationToken) => provider.GetRequiredService<ChatSettingsReloadValidator>()
            .FindConfigurationErrorsAsync(candidate, cancellationToken),
        ChatModelOptions.SectionName,
        provider.GetRequiredService<ILogger<ValidatedSettingsSnapshot<ChatModelOptions>>>()));
    builder.Services.AddSingleton<ISettingsSnapshot<ChatModelOptions>>(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<ChatModelOptions>>());
    builder.Services.AddHostedService(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<ChatModelOptions>>());

    // Registered once for both declarations, because it resolves by alias and the aliases are unique across them. It is
    // needed by whichever adapter exists, so it is registered when either does rather than by each of them.
    if (declaredEmbeddings?.IsConfigured is true || declaredChat?.IsConfigured is true)
    {
        builder.Services.AddSingleton<IProviderEndpointCredentialSource, ConfiguredProviderEndpointCredentialSource>();
        // Registered under the same condition and for the same reason: an instance that declared no endpoint at all
        // reaches none, so it has nothing to report about how it reaches them.
        builder.Services.AddHostedService<AiProviderTransportEncryptionWarning>();
    }

    if (declaredEmbeddings?.IsConfigured is true)
    {
        builder.Services.AddSingleton(provider => EmbeddingGenerationPlanMapper.Map(
            provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value)
            ?? throw new InvalidOperationException(
                "The embedding chain was declared at registration and is absent from the validated configuration."));
        builder.Services.AddEmbeddingProviderAdapter();
        // Beside the adapter rather than inside AddInfrastructure, because the units of work it registers resolve the
        // generator that call registers. An instance that declared no chain registers none of them and starts; one that
        // declared a chain registers them all and the workers below have something to resolve.
        builder.Services.AddEmailEmbeddingGeneration();
        builder.Services.AddHealthChecks()
            .Add(AiProviderHealthCheck.RegistrationFor(AiProviderRole.Embedding));
    }

    if (declaredChat?.IsConfigured is true)
    {
        // The source is a singleton because one published declaration maps to one plan; the plan itself is scoped
        // because one scope is one question, and reading it once per question is what keeps a run that has begun on the
        // plan it began with while the next one picks up an edited model.
        builder.Services.AddSingleton<IChatGenerationPlanSource, ChatGenerationPlanSource>();
        builder.Services.AddScoped(provider => provider.GetRequiredService<IChatGenerationPlanSource>().Current);
        builder.Services.AddChatProviderAdapter();
        builder.Services.AddMailAnsweringAgent();

        // The plan is registered here beside the endpoint it judges with; the filter itself is registered after
        // AddInfrastructure below, because it decorates the retrieval that call registers. Scoped for the reason the
        // generation plan is: the two numbers it carries are a lookup's, and whether the pass runs at all is the part
        // that was decided here and cannot follow a reload.
        if (declaredChat.RelevanceFilter.Enabled)
        {
            builder.Services.AddScoped(provider => PassageRelevanceFilterPlanMapper.Map(
                provider.GetRequiredService<ISettingsSnapshot<ChatModelOptions>>().Current,
                answeringBudget.Retrieval)
                ?? throw new InvalidOperationException(
                    "The relevance filter was enabled at registration and is absent from the configuration in force."));
        }

        // Readiness alone, and never worse than degraded. Neither provider serves a request path, so a failing one must
        // not take the instance out of traffic and must never reach the liveness probe: restarting the process cannot
        // fix a provider and would turn one outage into an outage plus a restart loop. The registration says all of
        // that once, for both roles.
        builder.Services.AddHealthChecks()
            .Add(AiProviderHealthCheck.RegistrationFor(AiProviderRole.Chat));
    }
    builder.Services.AddInfrastructure(
        provider => provider.GetRequiredService<DatabaseConnectionSettingsMapper>()
            .Map(provider.GetRequiredService<ISettingsSnapshot<PersistenceOptions>>().Current),
        string.IsNullOrWhiteSpace(configuredTextSearchConfiguration)
            ? PostgresTextSearchConfiguration.Default
            : PostgresTextSearchConfiguration.Create(configuredTextSearchConfiguration),
        answeringBudget);

    // After the retrieval it wraps, because the container resolves the last registration of a service type and the one
    // this decorates is added by the call above. An instance that declared no chat endpoint, or left the pass off,
    // registers nothing here and retrieves the fused ranking exactly as it did.
    if (declaredChat?.IsConfigured is true && declaredChat.RelevanceFilter.Enabled)
    {
        builder.Services.AddModelJudgedRetrieval();
    }

    // After the context is registered, because enrichment layers onto an existing registration rather than creating
    // one, and read from configuration directly for the same reason the text search configuration is: the value has to
    // be known before the container that would resolve an options snapshot exists. PersistenceOptions validates the
    // same key on start, which is what reports an out-of-range value to an operator.
    // Readiness alone. The database is a dependency a request needs, so an unreachable one must remove this instance
    // from traffic; it must never reach the liveness probe, because restarting a process cannot fix a database and
    // would turn one outage into an outage plus a restart loop.
    builder.AddDatabaseHealthAndTelemetry(
        TimeSpan.FromSeconds(builder.Configuration.GetValue(
            "Persistence:CommandTimeoutSeconds",
            HostApplicationBuilderExtensions.DefaultDatabaseCommandTimeoutSeconds)),
        probeTags: [HealthProbe.Readiness.Tag]);
    // Ahead of the workers so no unit of work reads or writes mail before the schema this build expects is proven, and
    // after the infrastructure that registers the inspector it resolves.
    builder.Services.AddHostedService<DatabaseSchemaStartupGate>();
    builder.Services.AddHostedService<MailSynchronizationCoordinator>();
    builder.Services.AddHostedService<MailExtractionBackfillWorker>();

    // Started only where a provider was declared. A deployment that declared none resolves no generator at all, so a
    // worker registered anyway would fail on the first message the backlog handed it rather than idle harmlessly.
    if (declaredEmbeddings?.IsConfigured is true)
    {
        builder.Services.AddHostedService<MailEmbeddingWorker>();

        // The same condition, because the backfill's unit of work is one message brought up to date by that same
        // generator. Whether it does anything is decided again at run time by whether a profile is active, which is the
        // state ADR 0006 makes the switch rather than a setting.
        builder.Services.AddHostedService<MailEmbeddingBackfillWorker>();
    }
    // Registered whether or not the endpoint is enabled, because it is the warning that decides whether it has anything
    // to say. Registering it conditionally would put the same condition in two places.
    builder.Services.AddHostedService<McpTransportAuthenticationWarning>();
    builder.Services.AddHostedService<McpTransportEncryptionWarning>();
    builder.Services.AddHostedService<ReverseProxyTrustWarning>();
    builder.Services.AddHostedService<TransportRateLimitingStartupReport>();

    // Registered beside the rate-limiting report and unconditionally for the same reason: each report is what decides
    // whether it has anything to say. They stay separate services because an operator turns either bound off alone.
    builder.Services.AddHostedService<TransportRequestTimeoutStartupReport>();
    builder.Services.AddHostedService<ConnectionLimitsStartupReport>();
    // Composed from the environment rather than resolved from the container, because the value it reports is one
    // OpenSSL read while it initialized and no configuration source can influence it afterwards. Registered
    // unconditionally for the same reason the warnings above are: the condition belongs in one place.
    builder.Services.AddHostedService(provider => OpenSslConfigurationWarning.FromEnvironment(
        provider.GetRequiredService<ILogger<OpenSslConfigurationWarning>>()));

    // Read before the surfaces, because it is the one posture they all sit behind: which peers this process accepts a
    // public scheme and host from. Read once for the same reason every section below is — the pipeline's
    // forwarded-header policy is composed from it, and the encryption warning states the posture this settles.
    var reverseProxySettings = ReverseProxyOptions.ReadFrom(builder.Configuration);
    builder.Services.AddSingleton(Options.Create(reverseProxySettings));

    var reverseProxyConfigurationErrors = reverseProxySettings.FindConfigurationErrors();

    if (reverseProxyConfigurationErrors.Count > 0)
    {
        throw new OptionsValidationException(
            ReverseProxyOptions.SectionName,
            typeof(ReverseProxyOptions),
            reverseProxyConfigurationErrors);
    }

    builder.Services.AddTrustedReverseProxy(reverseProxySettings);

    // Read beside the reverse-proxy posture rather than with the endpoint sections, because it is the other setting
    // that belongs to the process instead of to a surface: a connection is accepted before any routing has decided
    // which endpoint it was for. Bound strictly like every section that settles a security posture.
    var connectionLimitSettings = ConnectionLimitsOptions.ReadFrom(builder.Configuration);
    builder.Services.AddSingleton(Options.Create(connectionLimitSettings));

    var connectionLimitConfigurationErrors = connectionLimitSettings.FindConfigurationErrors();

    if (connectionLimitConfigurationErrors.Count > 0)
    {
        throw new OptionsValidationException(
            ConnectionLimitsOptions.SectionName,
            typeof(ConnectionLimitsOptions),
            connectionLimitConfigurationErrors);
    }

    // Read once and registered, so the value that decides the route is the one every consumer resolves. Whether the
    // endpoint exists is decided while the application is being built, before a container that could resolve a snapshot
    // exists, and a second read of a reloadable source could otherwise map the endpoint from one value while the missing
    // authentication was warned about from another.
    //
    // Bound strictly like the other security-sensitive sections: a misspelled "Enabeld" would leave the endpoint off
    // while an operator believed they had turned it on.
    var mcpEndpointSettings = McpEndpointOptions.ReadFrom(builder.Configuration);
    builder.Services.AddSingleton(Options.Create(mcpEndpointSettings));

    // Read once, like the MCP section and for the same reason. Administering this service and reading a mailbox
    // through it are different authorities, so the section is separate all the way down: its own listener, its own
    // credentials, and its own authorization servers. Bound strictly, so a misspelled key cannot leave a deployment
    // serving an administrative surface nobody meant to enable.
    var adminEndpointSettings = AdminEndpointOptions.ReadFrom(builder.Configuration);
    builder.Services.AddSingleton(Options.Create(adminEndpointSettings));

    // Read once, like the two sections above and for the same reason: it decides which sockets are opened and which
    // routes exist, both of which are settled while the application is being built. Bound strictly, so a misspelled key
    // cannot leave a deployment serving a posture nobody selected.
    var healthEndpointSettings = HealthEndpointOptions.ReadFrom(builder.Configuration);
    builder.Services.AddSingleton(Options.Create(healthEndpointSettings));

    // Every listener this process opens is bound in code, from the section of the surface it belongs to, so the host's
    // own ways of naming one decide nothing here and are refused rather than ignored. Read at the root, before any
    // section's own errors, because an operator who stated a port that no longer binds anything needs to be told that
    // first — every message below would otherwise describe a section they had not been using.
    var externalListenerConfigurationErrors = ExternalListenerConfiguration.FindConfigurationErrors(builder.Configuration);

    if (externalListenerConfigurationErrors.Count > 0)
    {
        throw new OptionsValidationException(
            ExternalListenerConfiguration.KestrelEndpointsSectionName,
            typeof(McpEndpointOptions),
            externalListenerConfigurationErrors);
    }

    // A process serving none of its surfaces opens no listener at all, and Kestrel answers that by binding its own
    // default address — and, where an ASP.NET Core development certificate happens to be installed, a TLS one beside it.
    // That is a socket no section describes, serving whatever a route happens to match, so it is refused here instead.
    if (!mcpEndpointSettings.Enabled && !adminEndpointSettings.Enabled && !healthEndpointSettings.Enabled)
    {
        throw new OptionsValidationException(
            McpEndpointOptions.SectionName,
            typeof(McpEndpointOptions),
            [
                $"No network surface is enabled: '{McpEndpointOptions.SectionName}:Enabled', "
                + $"'{AdminEndpointOptions.SectionName}:Enabled', and '{HealthEndpointOptions.SectionName}:Enabled' "
                + "are all off, so the process would serve nothing while still holding a socket. Enable the surface this "
                + "deployment exists to serve.",
            ]);
    }

    // Validated here rather than through ValidateOnStart, because the sections are read before a container exists and
    // the decisions they carry — which sockets to open, whether to map an endpoint, which scheme protects it — are taken
    // during composition. The secrets they name are proven separately, by the startup validator that proves every other
    // section's. Each section answers for itself first, so a message about a misspelled key is not delayed behind a
    // question about a socket the deployment may not even share.
    var mcpEndpointConfigurationErrors = mcpEndpointSettings.FindConfigurationErrors();

    if (mcpEndpointConfigurationErrors.Count > 0)
    {
        throw new OptionsValidationException(
            McpEndpointOptions.SectionName,
            typeof(McpEndpointOptions),
            mcpEndpointConfigurationErrors);
    }

    var adminEndpointConfigurationErrors = adminEndpointSettings.FindConfigurationErrors();

    if (adminEndpointConfigurationErrors.Count > 0)
    {
        throw new OptionsValidationException(
            AdminEndpointOptions.SectionName,
            typeof(AdminEndpointOptions),
            adminEndpointConfigurationErrors);
    }

    var healthEndpointConfigurationErrors = healthEndpointSettings.FindConfigurationErrors();

    if (healthEndpointConfigurationErrors.Count > 0)
    {
        throw new OptionsValidationException(
            HealthEndpointOptions.SectionName,
            typeof(HealthEndpointOptions),
            healthEndpointConfigurationErrors);
    }

    // Composed once, from what every enabled surface asked for. Surfaces may share a socket — which is what lets a
    // single-node deployment publish one port rather than three, and why both request-serving surfaces default to the
    // same ones — but they may not disagree about it, and this is where that is settled before anything binds.
    var composedListeners = ListenerComposition.Compose(
    [
        .. mcpEndpointSettings.DeclareListeners(),
        .. adminEndpointSettings.DeclareListeners(),
        .. healthEndpointSettings.DeclareListeners(),
    ]);

    if (composedListeners.Errors.Count > 0)
    {
        throw new OptionsValidationException(
            McpEndpointOptions.SectionName,
            typeof(McpEndpointOptions),
            composedListeners.Errors);
    }

    var servedSurfacesByPort = composedListeners.SurfacesByPort();

    // Registered whether or not the endpoint is enabled, because it is the warning that decides whether it has
    // anything to say — the same reason the MCP warnings above are registered unconditionally.
    builder.Services.AddHostedService<AdminTransportSecurityWarning>();

    // Registered here rather than beside the MCP warnings because it reads both surfaces, and unconditionally for the
    // same reason they are: the report is what decides whether either surface has a clear-text port to account for.
    builder.Services.AddHostedService<TransportClearTextRedirectReport>();

    // Mapped once, next to the decision that reads it, so the numbers the limiters are built from and the numbers the
    // startup report states are the same reading of the same settings. Null means an operator turned limiting off, or
    // the endpoint is not served at all, which is the one case in which no limiter is registered for it rather than one
    // configured to permit everything.
    var mcpRateLimits = mcpEndpointSettings is { Enabled: true, RateLimiting.Enabled: true }
        ? mcpEndpointSettings.RateLimiting.ToRateLimits()
        : null;

    var adminRateLimits = adminEndpointSettings is { Enabled: true, RateLimiting.Enabled: true }
        ? adminEndpointSettings.RateLimiting.ToRateLimits()
        : null;

    // Registered once with every bounded surface rather than once per endpoint. The process-wide limiter is a single
    // property of one options object, so a second registration would replace the first endpoint's concurrency limit
    // instead of adding to it — and it would do so silently, leaving whichever endpoint was registered first unbounded
    // in the half nothing else reports.
    var boundedSurfaces = new List<BoundedTransportSurface>();

    if (mcpRateLimits is not null)
    {
        boundedSurfaces.Add(new BoundedTransportSurface(TransportSurface.Mcp, mcpRateLimits));
    }

    if (adminRateLimits is not null)
    {
        boundedSurfaces.Add(new BoundedTransportSurface(TransportSurface.Admin, adminRateLimits));
    }

    if (boundedSurfaces.Count > 0)
    {
        builder.Services.AddTransportRateLimiting(boundedSurfaces);
    }

    // Mapped beside the rate limits and read the same way: null means an operator turned the ceiling off, or the
    // endpoint is not served, which are the two cases in which no policy is registered for it rather than one
    // permitting an unbounded request.
    var mcpRequestTimeout = mcpEndpointSettings is { Enabled: true, RequestTimeout.Enabled: true }
        ? mcpEndpointSettings.RequestTimeout.Duration
        : (TimeSpan?)null;

    var adminRequestTimeout = adminEndpointSettings is { Enabled: true, RequestTimeout.Enabled: true }
        ? adminEndpointSettings.RequestTimeout.Duration
        : (TimeSpan?)null;

    // Named policies rather than the framework's default policy, which would apply to every route in the process and
    // so to the probes as well: a readiness answer abandoned because a mailbox query was slow would take the instance
    // out of traffic for the one thing that was still working.
    if (mcpRequestTimeout is not null || adminRequestTimeout is not null)
    {
        builder.Services.AddRequestTimeouts(requestTimeoutOptions =>
        {
            if (mcpRequestTimeout is { } mcpTimeout)
            {
                requestTimeoutOptions.AddPolicy(TransportSurface.Mcp.RequestTimeoutPolicyName, mcpTimeout);
            }

            if (adminRequestTimeout is { } adminTimeout)
            {
                requestTimeoutOptions.AddPolicy(TransportSurface.Admin.RequestTimeoutPolicyName, adminTimeout);
            }
        });
    }

    const string adminCertificateStoreKey = "mailfathom.admin";

    // Registered whether or not any profile is configured, because the store is what the certificates are loaded into
    // and disposed from, and an unconfigured deployment simply loads none.
    builder.Services.AddSingleton(provider => new TransportServerCertificateStore(
        mcpEndpointSettings.Https,
        $"{McpEndpointOptions.SectionName}:{nameof(McpEndpointOptions.Https)}",
        provider.GetRequiredService<TlsServerCertificateLoader>(),
        provider.GetRequiredService<TimeProvider>(),
        provider.GetRequiredService<ILogger<TransportServerCertificateStore>>()));

    // Keyed, because two endpoints each own a store and the container has to tell them apart. Each store loads and
    // disposes only its own endpoint's material, which is what keeps one endpoint's certificates out of the other's
    // configuration — a shared socket is answered by consulting both in turn rather than by merging them.
    builder.Services.AddKeyedSingleton(adminCertificateStoreKey, (provider, _) => new TransportServerCertificateStore(
        adminEndpointSettings.Https,
        $"{AdminEndpointOptions.SectionName}:{nameof(AdminEndpointOptions.Https)}",
        provider.GetRequiredService<TlsServerCertificateLoader>(),
        provider.GetRequiredService<TimeProvider>(),
        provider.GetRequiredService<ILogger<TransportServerCertificateStore>>()));

    // Registered whether or not the transport terminates TLS, because the holder is what a certificate is loaded into
    // and disposed from, and a clear-text deployment simply loads none.
    builder.Services.AddSingleton<HealthEndpointCertificate>();

    if (mcpEndpointSettings.Enabled)
    {
        // The tools read the local mailbox copy through the use cases the infrastructure registration above already
        // added, so the protocol surface adds no port of its own.
        builder.Services.AddMailFathomServer();
        builder.Services.AddMcpTransportSecurity(mcpEndpointSettings);
    }

    if (adminEndpointSettings.Enabled)
    {
        builder.Services.AddAdminTransportSecurity(adminEndpointSettings);
    }

    // A separate callback from the listener binding below, and outside its condition, because the two decide different
    // things: that one opens the sockets each surface asked for, and this one bounds what the server accepts on all of
    // them at once. Several callbacks compose, so keeping them apart costs nothing and leaves each one about one
    // decision. Registering it is still the operator's to turn off, which is what the condition below reads.
    if (connectionLimitSettings.Enabled)
    {
        builder.WebHost.ConfigureKestrel(kestrelOptions =>
            kestrelOptions.Limits.MaxConcurrentConnections = connectionLimitSettings.MaxConcurrentConnections);
    }

    if (composedListeners.Listeners.Count > 0)
    {
        // The callback runs when the server is constructed, after the container exists, so the stores it reads are the
        // ones the composition root has already loaded. A profile-backed socket consults each endpoint's store in turn,
        // which is what lets two surfaces publish different domains on one port without either one's material entering
        // the other's section.
        builder.WebHost.ConfigureKestrel(kestrelOptions => TransportListenerBinder.Bind(
            kestrelOptions,
            composedListeners.Listeners,
            (listener, serverName) =>
                kestrelOptions.ApplicationServices.GetRequiredService<TransportServerCertificateStore>()
                    .Find(listener, serverName)
                ?? kestrelOptions.ApplicationServices
                    .GetRequiredKeyedService<TransportServerCertificateStore>(adminCertificateStoreKey)
                    .Find(listener, serverName),
            kestrelOptions.ApplicationServices.GetRequiredService<HealthEndpointCertificate>));
    }

    // Both endpoints call AddAuthentication, and each call sets the application's one default scheme, so the default
    // is otherwise whichever surface was registered last. It is stated here instead, because the thing that depends on
    // it is not obvious from either registration: UseAuthentication below populates HttpContext.User with the default
    // scheme, and the MCP rate limiter partitions on that user. Left to ordering, enabling the administrative endpoint
    // would silently collapse every authenticated MCP client into the shared anonymous bucket — no failure, just a
    // limit that stopped being per-client.
    if (mcpEndpointSettings is { Enabled: true, RequiresAuthentication: true })
    {
        builder.Services.Configure<AuthenticationOptions>(
            authenticationOptions => authenticationOptions.DefaultScheme = TransportSurface.Mcp.RoutingSchemeName);
    }

    var app = builder.Build();

    // Before the server starts rather than from a hosted service, because a hosted service could be started after the
    // web host and a certificate proven then would be proven after the listener was already open. A profile whose
    // material is missing, expired, or issued for another domain therefore fails startup with nothing listening.
    if (mcpEndpointSettings.Enabled && mcpEndpointSettings.TerminatesTls)
    {
        await app.Services.GetRequiredService<TransportServerCertificateStore>()
            .LoadAsync(app.Lifetime.ApplicationStopping);
    }

    if (adminEndpointSettings.Enabled && adminEndpointSettings.TerminatesTls)
    {
        await app.Services.GetRequiredKeyedService<TransportServerCertificateStore>(adminCertificateStoreKey)
            .LoadAsync(app.Lifetime.ApplicationStopping);
    }

    // For the same reason, and with the same outcome: a TLS transport whose material is unusable fails startup with
    // nothing listening rather than downgrading the probe port to clear text.
    await app.Services.GetRequiredService<HealthEndpointCertificate>()
        .LoadAsync(app.Lifetime.ApplicationStopping);

    // One authorization middleware serves every endpoint that requires it, and either endpoint may be the one that
    // adds it. Adding it twice would run every policy twice.
    var authorizationMiddlewareAdded = false;

    // The same holds for the rate limiter, which is one middleware serving whichever endpoints carry a policy. Adding
    // it twice would acquire a lease from both limiters twice and count one request as two.
    var rateLimiterMiddlewareAdded = false;

    // And for the request-timeout middleware, for the same reason again: it is one middleware reading whichever policy
    // the resolved endpoint names, and a second copy would start a second timer over the same request.
    var requestTimeoutMiddlewareAdded = false;

    // First, ahead of the exception handler and of every isolation check, so that nothing downstream ever reads a
    // scheme or host the proxy already corrected. Discovery, the challenge, and any address composed from a request
    // then agree with the name the client used, and a request from a peer this deployment does not trust passes
    // through with the scheme and host it arrived under.
    app.UseForwardedHeaders();

    app.UseExceptionHandler();

    // Composed from the sockets rather than from the surfaces, because a redirect is a property of the socket a request
    // arrived on. Two surfaces sharing one clear-text port contribute one listener between them, carrying the domains
    // both of them publish — which is what lets each redirect to an HTTPS port of its own from that shared socket, and
    // why one name published by both at different addresses is refused before composition reaches this.
    var clearTextRedirectListeners = composedListeners.Listeners
        .Where(static listener => listener.RedirectsClearText)
        .Select(static listener => new ClearTextRedirectListener(listener.Address.Port, listener.RedirectTargets))
        .ToArray();

    if (clearTextRedirectListeners.Length > 0)
    {
        // Ahead of the isolation middleware and every route, which is what makes a redirecting socket serve nothing but
        // the redirect. Behind it, an administrative path arriving on a redirect port would be answered by isolation
        // with a 404 — a listener refusing a path it does not serve — and the client would read the endpoint as gone
        // rather than as moved.
        app.UseClearTextRedirectToHttps(new ClearTextRedirectTargets(clearTextRedirectListeners));
    }

    // Ahead of everything any surface adds, so a request for a surface this listener does not serve is refused before
    // it reaches CORS, authentication, the client-certificate check, or the rate limiter — and a probe that arrived
    // where the probes are not served is refused before it can report dependency state to whoever can reach it.
    app.UseSurfaceIsolation(servedSurfacesByPort);

    if (healthEndpointSettings.Enabled)
    {
        // A probe reports healthy over no checks at all, because the aggregate of nothing is healthy. Asserting the
        // composed result rather than the wiring is what catches a tag that stopped matching: readiness answering
        // without consulting the database would keep an instance in traffic that cannot serve a request.
        var unansweredProbes = HealthProbeEndpoints.FindUnansweredProbes(
            app.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations);

        if (unansweredProbes.Count > 0)
        {
            throw new OptionsValidationException(
                HealthEndpointOptions.SectionName,
                typeof(HealthEndpointOptions),
                unansweredProbes);
        }

        app.MapHealthProbes();
    }

    if (mcpEndpointSettings.Enabled)
    {
        // CORS first, so a browser's preflight is answered by the middleware that owns preflight rather than reaching a
        // check written for real requests. The origin check then runs ahead of authentication, because whether this
        // deployment serves a page's origin does not depend on which credential the page attached.
        app.UseCors();

        if (mcpEndpointSettings.AllowsOAuth)
        {
            // The policy reaches the MCP route as endpoint metadata, and the protected resource metadata document has
            // none to carry it: the authentication handler publishes it instead of a mapped route. A browser client
            // reads that document before it holds any credential, so without the policy applied to its path the one
            // response that says where to authorize is the one a page cannot read.
            var protectedResourceMetadataPath = ProtectedResourceMetadataAddress.PathFor(
                mcpEndpointSettings.OAuthMethods()[0].CanonicalResource());

            app.UseWhen(
                context => context.Request.Path.Equals(protectedResourceMetadataPath, StringComparison.OrdinalIgnoreCase),
                metadataDocument => metadataDocument.UseCors(McpTransportSecurityExtensions.CorsPolicyName));
        }

        app.UseMcpOriginValidation();

        if (mcpEndpointSettings.ClientCertificateProfiles.Count > 0)
        {
            // Ahead of authentication, because which client application is calling and which credential it presents are
            // separate questions: a request from a program this deployment does not serve is turned away before any
            // credential is read.
            app.UseMcpClientCertificateValidation(mcpEndpointSettings.ToClientCertificateTrustProfiles());
        }

        var mcpEndpoint = app
            .MapMcp(McpEndpointRoute.Path)
            .RequireCors(McpTransportSecurityExtensions.CorsPolicyName);

        // Mapped with the MCP surface because it belongs to it: the links it answers are minted by an MCP tool, and
        // serving it here gives it that endpoint's transport, its rate limits, and its enablement without a listener of
        // its own. What it does not inherit is what the two middlewares above scope to the protocol path themselves —
        // the origin allow-list and the client-certificate profiles — and that is the right side of the line: both ask
        // which program is calling, which is the question this route deliberately does not have an answer to. It
        // carries no authorization either, since the signed capability in the URL is what admits a request and the
        // things that fetch files cannot attach an MCP credential, which is why it is mapped outside the group the
        // access policy is applied to below.
        var attachmentDownload = app.MapEmailAttachmentDownload();

        if (mcpEndpointSettings.RequiresAuthentication)
        {
            // Authentication also serves the protected resource metadata document, which the MCP authentication scheme
            // publishes as a request handler rather than as a route, so the middleware runs whether or not the request
            // that follows carries a credential.
            //
            // Scoped away from the administrative routes rather than added globally. This middleware authenticates with
            // the application's default scheme, pinned above to the MCP surface's, so an administrative request
            // reaching it would have its credential compared against the MCP endpoint's keys before the administrative
            // policy ever ran. Nothing would be disclosed by that — the comparison is constant-time and the result is
            // discarded — but a credential provisioned for one surface must not be offered to the other's handlers.
            app.UseWhen(
                context => !context.Request.Path.StartsWithSegments(AdminEndpointOptions.RoutePrefix),
                mcpBranch => mcpBranch.UseAuthentication());
        }

        if (mcpRequestTimeout is not null || adminRequestTimeout is not null)
        {
            // Ahead of the rate limiter, so the ceiling covers the time a request spends waiting for a lease as well as
            // the time it spends being served. That wait is nothing under the default queue limits of zero, and is the
            // whole point of the ordering under a configured queue: a request queued for a caller's tokens is already
            // holding a concurrency permit, so leaving it outside the ceiling would leave the one wait that can last
            // until the next replenishment unbounded.
            app.UseRequestTimeouts();
            requestTimeoutMiddlewareAdded = true;
        }

        if (boundedSurfaces.Count > 0)
        {
            // Behind authentication, so the MCP endpoint's per-client limit is counted under the identity that
            // established rather than under something the caller chose, and ahead of authorization, so a request that is
            // about to be refused for its credential still spends anonymous capacity — otherwise a flood of bad
            // credentials would be the one kind of traffic an endpoint served without limit. It is added here whenever
            // either endpoint is bounded, because this is the point that satisfies both orderings: an administrative
            // request reaches it as well, since what the branch above keeps away from those routes is the
            // authentication middleware rather than the pipeline.
            app.UseRateLimiter();
            rateLimiterMiddlewareAdded = true;
        }

        if (mcpRateLimits is not null)
        {
            // On the endpoint for the same reason authorization is: the readiness and liveness endpoints have to keep
            // answering while this one is refusing. The process-wide half of the policy cannot be attached here, because
            // an endpoint resolves one limiter, so it rides on the global limiter and excludes every other route itself.
            mcpEndpoint.RequireRateLimiting(TransportSurface.Mcp.RateLimitingPolicyName);

            // The same per-caller policy on the download route, which admits no credential and therefore spends the
            // surface's shared anonymous bucket. That is the point rather than a limitation: an unauthenticated route
            // serving mail content is exactly the one that must not be unbounded. The process-wide half reaches it as
            // well, because the surface names this prefix among the ones it serves, so a redemption takes a permit
            // from the same concurrency limiter the protocol route does rather than opening an unbounded second door
            // onto the same message store and MIME parser.
            attachmentDownload.RequireRateLimiting(TransportSurface.Mcp.RateLimitingPolicyName);
        }

        if (mcpRequestTimeout is not null)
        {
            // On the endpoint rather than as the default policy, for the reason the limiter is: the probes answer on
            // routes this ceiling must never reach.
            mcpEndpoint.WithRequestTimeout(TransportSurface.Mcp.RequestTimeoutPolicyName);

            // And on the download route, for the reason the rate limit is on it: it takes a permit from the same
            // concurrency limiter, and it is the one route here that holds a response stream open for as long as its
            // reader takes. A client reading just above Kestrel's minimum response rate is the case the ceiling exists
            // for, and it needs no credential to be that client.
            attachmentDownload.WithRequestTimeout(TransportSurface.Mcp.RequestTimeoutPolicyName);
        }

        if (mcpEndpointSettings.RequiresAuthentication)
        {
            app.UseAuthorization();
            authorizationMiddlewareAdded = true;

            // On the endpoint rather than as a fallback policy, so the readiness response and the health endpoints keep
            // answering unauthenticated while everything the MCP route exposes is covered by the one requirement it
            // carries. Under the stateless transport that route is the post alone; a get or a delete is not mapped at
            // all, so there is no second entry into the protocol surface for a requirement to miss.
            mcpEndpoint.RequireAuthorization(TransportSurface.Mcp.AccessPolicyName);
        }
    }

    if (adminEndpointSettings.Enabled)
    {
        // Ahead of the limiter for the reason it is ahead of it on the MCP branch, so a request queued for a lease is
        // inside the ceiling rather than outside it.
        if (adminRequestTimeout is not null && !requestTimeoutMiddlewareAdded)
        {
            app.UseRequestTimeouts();
        }

        // Ahead of the authorization middleware below, which is what judges this surface's credential, so a request
        // about to be refused for a wrong key has already spent capacity. That ordering is the whole point of bounding
        // this endpoint: unbounded key guessing is what it is exposed to, and the guesses are the traffic authorization
        // turns away.
        if (adminRateLimits is not null && !rateLimiterMiddlewareAdded)
        {
            app.UseRateLimiter();
        }

        // The administrative routes carry no authentication middleware of their own. That middleware authenticates with
        // the application's default scheme, which belongs to the MCP surface; the authorization middleware instead
        // authenticates with the schemes the policy names, which are this surface's. So requiring the policy is both
        // what admits a caller and what establishes who they are.
        //
        // That is also why this surface's per-caller partition is one bucket rather than one per key: there is no
        // identity to partition on until authorization has run, and running the limiter behind it would serve the
        // guessing this exists to bound. The bucket is the endpoint's, and its capacity is sized as such.
        if (adminEndpointSettings.RequiresAuthentication && !authorizationMiddlewareAdded)
        {
            app.UseAuthorization();
        }

        var adminApi = app.MapAdminApi();

        if (adminRateLimits is not null)
        {
            adminApi.RequireRateLimiting(TransportSurface.Admin.RateLimitingPolicyName);
        }

        if (adminRequestTimeout is not null)
        {
            adminApi.WithRequestTimeout(TransportSurface.Admin.RequestTimeoutPolicyName);
        }

        if (adminEndpointSettings.RequiresAuthentication)
        {
            adminApi.RequireAuthorization(TransportSurface.Admin.AccessPolicyName);
        }

        if (adminEndpointSettings.AllowsOAuth)
        {
            // Outside the group the requirement was attached to, and deliberately: its reader is a client that has no
            // credential yet and is reading this to find out where to obtain one.
            app.MapAdminProtectedResourceMetadata(adminEndpointSettings.OAuthMethods());
        }
    }

    await app.RunAsync();

    bootstrapLogger.RecordHostStopped();
}
catch (Exception exception)
{
    // The exception leaves unchanged, so the runtime still writes it to standard error and the exit code is the one
    // an unhandled failure produces. The catch exists only to add the record that survives the process ending.
    bootstrapLogger.RecordHostFailed(exception);

    throw;
}
