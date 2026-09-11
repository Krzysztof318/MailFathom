// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Enrichment;

/// <summary>Covers the last stage of the arrival pipeline: what it writes down, and what stops it.</summary>
/// <remarks>
/// Which mail reaches the pass is the store's predicate and is asserted where that predicate lives. What is asserted
/// here is the pass's own contract — that a settled answer is committed whether or not it found anything to say, that a
/// withheld one writes nothing and ends the pass, and that a full batch is reported as work remaining rather than
/// walked to the end of the mailbox.
/// </remarks>
public sealed class MailEnrichmentPassTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailUser.Deployment, MailAccountId.Create("work"));

    private static readonly DateTimeOffset DerivedAt = new(2026, 9, 6, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_MailAwaitingADerivation_WritesOneRecordPerMessage()
    {
        // Arrange
        var first = Enrichable();
        var second = Enrichable();
        var store = StoreReturning([first, second]);
        var pass = CreatePass(store, EnricherAnswering(_ => EmailEnrichmentDerivation.Settled([Sense()])));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, report.DerivedEmailCount);
        Assert.Equal(2, report.MarkedEmailCount);
        Assert.Null(report.StoppedBy);
        Assert.False(report.EmailsRemain);
        await store.Received(1).SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Is<EmailEnrichment>(enrichment =>
                enrichment!.StoredEmailId == first.StoredEmailId && enrichment.DerivedAt == DerivedAt),
            Arg.Any<CancellationToken>());
        await store.Received(1).SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Is<EmailEnrichment>(enrichment => enrichment!.StoredEmailId == second.StoredEmailId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A message a derivation had nothing to say about is settled, so it never returns to the queue.</summary>
    [Fact]
    public async Task RunAsync_ADerivationThatFoundNothingToSay_StillWritesTheRecord()
    {
        // Arrange
        var store = StoreReturning([Enrichable()]);
        var pass = CreatePass(store, EnricherAnswering(_ => EmailEnrichmentDerivation.Settled([])));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.DerivedEmailCount);
        Assert.Equal(0, report.MarkedEmailCount);
        await store.Received(1).SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Is<EmailEnrichment>(enrichment => enrichment!.Marks.Count == 0),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Every reason a derivation is withheld outlives one message, so the pass stops on the first rather than buying
    /// the same answer for each message behind it — and the message stays outstanding for the next run.
    /// </summary>
    [Theory]
    [InlineData(EmailEnrichmentWithholding.NotActivated)]
    [InlineData(EmailEnrichmentWithholding.AllowanceExhausted)]
    [InlineData(EmailEnrichmentWithholding.ProviderUnavailable)]
    public async Task RunAsync_ADerivationWithheld_WritesNothingAndEndsThePass(EmailEnrichmentWithholding withholding)
    {
        // Arrange
        var store = StoreReturning([Enrichable(), Enrichable()]);
        var enricher = EnricherAnswering(_ => EmailEnrichmentDerivation.Withholding(withholding));
        var pass = CreatePass(store, enricher);

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(withholding, report.StoppedBy);
        Assert.Equal(0, report.DerivedEmailCount);
        Assert.True(report.EmailsRemain);
        await enricher.Received(1).DeriveAsync(
            Arg.Any<EnrichableEmail>(),
            Arg.Any<MailUserLanguage>(),
            Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<EmailEnrichment>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>What one message's derivation settled is durable before the next message costs a provider call.</summary>
    [Fact]
    public async Task RunAsync_ThePassStoppingHalfWayThrough_KeepsWhatEarlierMessagesSettled()
    {
        // Arrange
        var first = Enrichable();
        var second = Enrichable();
        var store = StoreReturning([first, second]);
        var pass = CreatePass(
            store,
            EnricherAnswering(email => email.StoredEmailId == first.StoredEmailId
                ? EmailEnrichmentDerivation.Settled([Sense()])
                : EmailEnrichmentDerivation.Withholding(EmailEnrichmentWithholding.ProviderUnavailable)));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.DerivedEmailCount);
        Assert.Equal(EmailEnrichmentWithholding.ProviderUnavailable, report.StoppedBy);
        await store.Received(1).SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Is<EmailEnrichment>(enrichment => enrichment!.StoredEmailId == first.StoredEmailId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NothingAwaitingADerivation_ReportsAnEmptyPass()
    {
        // Arrange
        var enricher = EnricherAnswering(_ => EmailEnrichmentDerivation.Settled([]));
        var pass = CreatePass(StoreReturning([]), enricher);

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(report.IsEmpty);
        Assert.False(report.EmailsRemain);
        await enricher.DidNotReceiveWithAnyArgs().DeriveAsync(
            Arg.Any<EnrichableEmail>(),
            Arg.Any<MailUserLanguage>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A mailbox turned on for the first time is drained over successive runs rather than in one pass, which is what
    /// makes the backfill bounded: the pass ends on its own bound and says that more is waiting.
    /// </summary>
    [Fact]
    public async Task RunAsync_AFullBatch_EndsOnItsBoundAndReportsMailRemaining()
    {
        // Arrange
        var store = StoreReturning(
            [.. Enumerable.Range(0, MailEnrichmentPass.MaximumEmailsPerPass).Select(_ => Enrichable())]);
        var pass = CreatePass(store, EnricherAnswering(_ => EmailEnrichmentDerivation.Settled([])));

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailEnrichmentPass.MaximumEmailsPerPass, report.DerivedEmailCount);
        Assert.True(report.EmailsRemain);
        await store.Received(1).GetEmailsAwaitingEnrichmentAsync(
            Account,
            MailEnrichmentPass.MaximumEmailsPerPass,
            MailEnrichmentPass.MaximumPassagesPerEmail,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The default deployment, where the switch is answered before anything is read. A pass that queried first would
    /// scan for work nothing was going to do, once per account per run, for the life of every instance that never
    /// turned enrichment on — and would say so in a log line each time.
    /// </summary>
    [Fact]
    public async Task RunAsync_ADeploymentThatDerivesNothing_IssuesNoQueryAndReportsNothing()
    {
        // Arrange
        var store = StoreReturning([Enrichable()]);
        var enricher = Substitute.For<IEmailEnricher>();
        enricher.IsActive.Returns(false);
        var pass = CreatePass(store, enricher);

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(report.IsEmpty);
        Assert.Equal(EmailEnrichmentWithholding.NotActivated, report.StoppedBy);
        Assert.False(report.EmailsRemain);
        await store.DidNotReceiveWithAnyArgs().GetEmailsAwaitingEnrichmentAsync(
            Arg.Any<MailAccountIdentity>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A condition that stopped a working pass is worth a line, unlike the switch that was never turned on.</summary>
    [Theory]
    [InlineData(EmailEnrichmentWithholding.AllowanceExhausted)]
    [InlineData(EmailEnrichmentWithholding.ProviderUnavailable)]
    public async Task RunAsync_APassAWithholdingStopped_IsReportedRatherThanPassedOver(
        EmailEnrichmentWithholding withholding)
    {
        // Arrange
        var enricher = EnricherAnswering(_ => EmailEnrichmentDerivation.Withholding(withholding));
        var pass = CreatePass(StoreReturning([Enrichable()]), enricher);

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(report.IsEmpty);
        Assert.Equal(withholding, report.StoppedBy);
    }

    private static EnrichableEmail Enrichable() =>
        new(
            StoredEmailId.Create(Guid.CreateVersion7()),
            "The racking quotation",
            new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero),
            [new EnrichablePassage(EmailChunkId.Create(Guid.CreateVersion7()), 0, "the quotation is attached")]);

    private static EmailEnrichmentMark Sense() =>
        EmailEnrichmentMark.Create(
            EmailEnrichmentAspect.Sense,
            "a racking quotation",
            "the passage attaches one",
            [EmailChunkId.Create(Guid.CreateVersion7())],
            EmailEnrichmentProvenance.FromAgent("mailfathom-email-enrichment"));

    private static IStoredEmailEnrichmentStore StoreReturning(IReadOnlyList<EnrichableEmail> batch)
    {
        var store = Substitute.For<IStoredEmailEnrichmentStore>();
        store
            .GetEmailsAwaitingEnrichmentAsync(Account, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(batch);

        return store;
    }

    private static IEmailEnricher EnricherAnswering(Func<EnrichableEmail, EmailEnrichmentDerivation> answer)
    {
        var enricher = Substitute.For<IEmailEnricher>();
        enricher.IsActive.Returns(true);
        enricher
            .DeriveAsync(Arg.Any<EnrichableEmail>(), Arg.Any<MailUserLanguage>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(answer(call.ArgAt<EnrichableEmail>(0))));

        return enricher;
    }

    /// <summary>
    /// Every message in one pass belongs to one person, and what that person reads is what every reading derived from
    /// their mail is written in — the pass is where the two meet, because the derivation is handed a language rather
    /// than resolving one.
    /// </summary>
    [Theory]
    [InlineData(MailUserLanguage.Polish)]
    [InlineData(MailUserLanguage.English)]
    public async Task RunAsync_AnAccountWhoseOwnerReadsALanguage_DerivesInIt(MailUserLanguage language)
    {
        // Arrange
        var store = StoreReturning([Enrichable()]);
        var enricher = EnricherAnswering(_ => EmailEnrichmentDerivation.Settled([]));
        var pass = CreatePass(store, enricher, language);

        // Act
        await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        await enricher.Received(1).DeriveAsync(
            Arg.Any<EnrichableEmail>(),
            language,
            Arg.Any<CancellationToken>());
    }

    /// <summary>Answers one language for whoever is asked about, which is what a pass over one account's mail needs.</summary>
    private static IMailUserLanguages LanguagesAnswering(MailUserLanguage language)
    {
        var languages = Substitute.For<IMailUserLanguages>();
        languages.ForUser(Arg.Any<MailUserId>()).Returns(language);

        return languages;
    }

    /// <summary>Composes the pass over a deployment with no scanner switched on, which is the ordinary shape.</summary>
    /// <remarks>
    /// What a scanner does to a passage is asserted where the derivation sends one, because the pass hands the guard to
    /// the derivation rather than scanning anything itself.
    /// </remarks>
    private static MailEnrichmentPass CreatePass(
        IStoredEmailEnrichmentStore store,
        IEmailEnricher enricher,
        MailUserLanguage language = MailUserLanguage.English)
    {
        var timeProvider = new FakeTimeProvider(DerivedAt);
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory
            .BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IPersistenceSession>());

        return new MailEnrichmentPass(
            store,
            enricher,
            LanguagesAnswering(language),
            SensitiveContentEgressGuards.Inactive(),
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                timeProvider),
            timeProvider);
    }
}
