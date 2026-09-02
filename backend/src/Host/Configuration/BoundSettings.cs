// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Answering;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.DataEncryption;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Host.Configuration.Jobs;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Persistence;
using MailFathom.Host.Configuration.Rules;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Infrastructure.Resilience;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration;

/// <summary>Binds every configuration section whose value is read through the options framework.</summary>
/// <remarks>
/// <para>
/// Declared apart from the composition root because two things register it: the host, once, over the configuration the
/// process was composed from, and a configuration write, over the configuration its candidate document would produce.
/// A write is judged by the rules a start applies, so the rules are stated once and both readers of them get the same
/// answer — a second list beside this one is exactly how a setting would come to bind at startup and not at a write.
/// </para>
/// <para>
/// Registration alone is what this does. Which of the sections are validated eagerly is each section's own decision,
/// expressed by whether it carries <c>ValidateOnStart</c>, and a candidate is judged by that same choice: a section
/// deliberately outside the startup gate is outside a write's gate too, rather than acquiring a stricter rule at the
/// one moment nobody is watching.
/// </para>
/// </remarks>
internal static class BoundSettings
{
    /// <summary>The section the outbound dependency budgets are read from, which owns no options type of its own.</summary>
    private const string ResilienceSectionName = "Resilience";

    /// <summary>Registers the bound settings and the validators that judge them.</summary>
    /// <param name="services">The container the options are registered in.</param>
    /// <param name="configuration">The configuration the sections are bound from.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static void AddTo(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bound strictly: mail transport is security-sensitive, and a misspelled key such as a singular
        // "PermittedAuthenticationMechanism" would otherwise be ignored and silently replaced by the default allow-list.
        services.AddOptions<MailSynchronizationOptions>()
            .Bind(
                configuration.GetSection(MailSynchronizationOptions.SectionName),
                binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // The one mail synchronization rule that needs the current date, which no attribute on a bound options graph can
        // reach, arrives through the options framework's own validator seam rather than as a second validation mechanism.
        services.AddSingleton<IValidateOptions<MailSynchronizationOptions>, MailSynchronizationWindowValidator>();
        // The host stops awaiting StopAsync once its own shutdown budget expires, so a drain configured beyond that budget
        // would be accepted and never honored: the process would exit with the work still running. The budget is therefore
        // derived from the configured drain instead of being left on the framework default. Read from configuration
        // directly for the same reason the text search configuration below is — the value has to be known while the host
        // is being built, before a container that could resolve an options snapshot exists. It is restart-required, which
        // is what a shutdown budget is by nature.
        services.Configure<HostOptions>(hostOptions => hostOptions.ShutdownTimeout =
            MailSynchronizationOptions.ResolveHostShutdownBudget(configuration.GetValue(
                $"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.ShutdownDrainTimeout)}",
                new MailSynchronizationOptions().ShutdownDrainTimeout)));
        // Bound strictly for the same reason as mail transport: a misspelled "Passwrod" would leave the secret block
        // undiscovered, start the host on a passwordless connection string, and surface as an authentication failure later.
        services.AddOptions<PersistenceOptions>()
            .Bind(
                configuration.GetSection(PersistenceOptions.SectionName),
                binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // Bound strictly like the blocks above: a misspelled "SnippetsPerEmails" would leave the configured bound
        // undiscovered and search would quietly keep showing the default amount of every matched message.
        services.AddOptions<MailboxSearchOptions>()
            .Bind(
                configuration.GetSection(MailboxSearchOptions.SectionName),
                binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<MailExtractionBackfillOptions>()
            .Bind(
                configuration.GetSection(MailExtractionBackfillOptions.SectionName),
                binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<EmailContentOptions>()
            .Bind(
                configuration.GetSection(EmailContentOptions.SectionName),
                binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<MailDeliveryOptions>()
            .Bind(
                configuration.GetSection(MailDeliveryOptions.SectionName),
                binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // A configuration root of its own rather than a block inside Persistence, because what it selects is whether
        // message payloads are in the database at all: a deployment writing them into a bucket still runs every metadata
        // row, every index, and every job through PostgreSQL. A root is also what gives the endpoint's credentials their
        // own secret-name uniqueness scope. An absent section is the database backend, which is what every deployment
        // that has never heard of the setting is already running.
        services.AddOptions<ContentStorageOptions>()
            .Bind(
                configuration.GetSection(ContentStorageOptions.SectionName),
                binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // A configuration root of its own rather than a block inside the feature that first needed it. The address clients
        // reach this deployment at is a property of the installation, so an operator answers it once and whatever else has
        // to hand back an absolute address later reads the same key instead of adding a second one beside it.
        services.AddOptions<DeploymentOptions>()
            .Bind(
                configuration.GetSection(DeploymentOptions.SectionName),
                binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // A configuration root of its own, because what a deployment embeds with is a property of that deployment rather
        // than of its database or its mail accounts, and a root is also what gives its keys their own secret-name
        // uniqueness scope. An absent section is a deployment that embeds nothing and serves lexical search, which
        // ADR 0006 makes a supported state rather than a startup failure.
        services.AddOptions<EmbeddingOptions>()
            .Bind(
                configuration.GetSection(EmbeddingOptions.SectionName),
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
        services.AddOptions<ChatModelOptions>()
            .Bind(
                configuration.GetSection(ChatModelOptions.SectionName),
                binderOptions => binderOptions.ErrorOnUnknownConfiguration = true);
        // A configuration root of its own beside Chat rather than a block inside it, because the two answer different
        // questions: Chat says which endpoint generates text and what one call to it may carry, while this says what
        // answering a question is allowed to cost and how much of a mailbox may leave the process to do it. Unlike the
        // provider sections, an absent section is not an absent capability — every deployment has these ceilings, and
        // writing nothing takes the conservative defaults rather than none.
        services.AddOptions<MailAnsweringOptions>()
            .Bind(
                configuration.GetSection(MailAnsweringOptions.SectionName),
                binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // Beside the declaration rather than inside it: what an instance embeds with is a commitment, and how fast it works
        // through the mail it already had is a rate an operator changes while watching a provider bill.
        services.AddOptions<EmbeddingBackfillOptions>()
            .Bind(
                configuration.GetSection(EmbeddingBackfillOptions.SectionName),
                binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // A configuration root of its own, because durable background work is a mechanism every consumer shares rather than
        // a property of any one of them: what a job does belongs to the feature that enqueues it, and how much of the
        // instance the queue may take belongs here.
        services.AddOptions<JobQueueOptions>()
            .Bind(
                configuration.GetSection(JobQueueOptions.SectionName),
                binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // A configuration root of its own rather than a section of Persistence: the database is the first thing sealed
        // under the ring and there is no reason it is the last, and a root is also what gives the key material its own
        // secret-name uniqueness scope. ADR 0005 records the whole decision.
        services.AddOptions<DataEncryptionOptions>()
            .Bind(
                configuration.GetSection(DataEncryptionOptions.SectionName),
                binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // A configuration root of its own, because what a deployment scans mail for before copying it or handing it out is
        // a property of that deployment rather than of its database, its accounts, or its providers, and the two switches
        // it holds reach several of those at once. Both are off by default, and an absent section is that default rather
        // than a startup failure.
        services.AddOptions<SensitiveContentOptions>()
            .Bind(
                configuration.GetSection(SensitiveContentOptions.SectionName),
                binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // The category and rule names an operator wrote are judged against what the registered scanners declare, which no
        // attribute on the bound graph can reach. Registered whatever the switches say, because a switch turned on with no
        // detector behind it is exactly what it refuses.
        services.AddSingleton<IValidateOptions<SensitiveContentOptions>, SensitiveContentCatalogValidator>();
        // Bound strictly for the reason the rule section is: a misspelled key here would leave classification looking
        // configured while it was off, and an operator reading their own file as proof of it.
        services.AddOptions<SpamClassificationOptions>()
            .Bind(
                configuration.GetSection(SpamClassificationOptions.SectionName),
                binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // Whether the junk folder a filing names exists is a claim about the synchronization section, which no attribute on
        // this graph can reach. Registered whatever the switches say, because a filing switched on with no folder behind it
        // is exactly what it refuses.
        services.AddSingleton<IValidateOptions<SpamClassificationOptions>>(new SpamJunkFolderValidator(configuration));
        // Rules are authored in configuration rather than in a table, which ADR 0010 records: what an instance will do to a
        // mailbox is then reviewable in a diff before it runs and reproducible from a repository afterwards. Bound strictly
        // for the reason mail transport is — a misspelled key would otherwise be ignored, and the rule it belonged to would
        // go on running while meaning something its author did not write. What the declarations themselves have to satisfy
        // is not an attribute on this graph and is stated in <see cref="ComposedSettings" /> instead.
        services.AddOptions<MailRulesOptions>()
            .Bind(
                configuration.GetSection(MailRulesOptions.SectionName),
                binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // The non-HTTP dependency budgets, registered from here rather than from the composition root so that a
        // configuration write is judged by them too: they are restart-required, so a candidate carrying an attempt count
        // past its range or a section naming no dependency class would commit and stop the next start. The registration
        // is the whole of the rule — it binds each class strictly under ValidateOnStart and refuses an unknown section at
        // registration time — and it freezes the section it reads, which is what keeps the restart-required
        // classification true whichever configuration it is handed. HttpClient traffic is already wrapped once by the
        // standard resilience handler in the service defaults, which is why this covers the non-HTTP classes only.
        services.AddOutboundResiliencePipelines(configuration.GetSection(ResilienceSectionName));
    }
}
