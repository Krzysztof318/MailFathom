// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Infrastructure.SensitiveContent.PersonalData;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.SensitiveContent.PersonalData;

/// <summary>Covers the one question asked before the host finishes coming up: can this analyzer answer at all?</summary>
/// <remarks>
/// Every failure this class produces would otherwise surface as no findings, and no findings is indistinguishable from a
/// clean mailbox. That is why the probe fails startup rather than logging, and why its message names the configuration key
/// an operator would edit rather than the address that key resolved to, which stays on the failure's own property.
/// </remarks>
public sealed class PresidioAnalyzerProbeTests
{
    [Fact]
    public async Task VerifyAvailableAsync_AnalyzerRecognisingEveryConfiguredCategory_Passes()
    {
        // Arrange
        using var context = AnalyzerAnswering(SupportedEntitiesOf(PersonalDataScanningPlans.Default));

        // Act
        await context.Probe.VerifyAvailableAsync(CancellationToken.None);

        // Assert
        var recorded = Assert.Single(context.Handler.RecordedRequests);
        Assert.Equal(HttpMethod.Get, recorded.Method);
        Assert.Equal(
            "http://analyzer.invalid:3000/supportedentities?language=en",
            recorded.RequestUri?.ToString());
    }

    /// <summary>
    /// A narrower registry costs recall inside a category that still works, which is the analyzer operator's own trade to
    /// make. Only a category with nothing left is refused.
    /// </summary>
    [Fact]
    public async Task VerifyAvailableAsync_AnalyzerMissingSomeEntitiesOfACategory_Passes()
    {
        // Arrange
        var plan = PersonalDataScanningPlans.For([PersonalDataScanningPlans.Category("BankAccount")]);
        using var context = AnalyzerAnswering("""["IBAN_CODE"]""", plan);

        // Act
        await context.Probe.VerifyAvailableAsync(CancellationToken.None);

        // Assert
        Assert.Single(context.Handler.RecordedRequests);
    }

    /// <summary>A category the analyzer recognises nothing of would be scanned for and never found, which reads as a clean message.</summary>
    [Fact]
    public async Task VerifyAvailableAsync_CategoryTheAnalyzerRecognisesNothingOf_RefusesToStart()
    {
        // Arrange
        var plan = PersonalDataScanningPlans.For(
            [PersonalDataScanningPlans.Category("PaymentCard"), PersonalDataScanningPlans.Category("HealthIdentifier")]);
        using var context = AnalyzerAnswering("""["CREDIT_CARD"]""", plan);

        // Act
        var failure = await Assert.ThrowsAsync<PersonalDataAnalyzerUnavailableException>(
            () => context.Probe.VerifyAvailableAsync(CancellationToken.None));

        // Assert
        Assert.Contains("HealthIdentifier", failure.Message, StringComparison.Ordinal);
        Assert.Contains("SensitiveContent:PersonalDataAnalyzer:Endpoint", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>An address that answers something other than an analyzer, and an analyzer with no model for the language, land here.</summary>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task VerifyAvailableAsync_AnalyzerRefusingTheProbe_RefusesToStartNamingTheStatus(HttpStatusCode status)
    {
        // Arrange
        using var context = AnalyzerAnswering("""{"error":"No matching recognizers were found"}""", status: status);

        // Act
        var failure = await Assert.ThrowsAsync<PersonalDataAnalyzerUnavailableException>(
            () => context.Probe.VerifyAvailableAsync(CancellationToken.None));

        // Assert
        Assert.Contains(((int)status).ToString(CultureInfo.InvariantCulture), failure.Message, StringComparison.Ordinal);
        Assert.Equal("http://analyzer.invalid:3000/", failure.Endpoint);
    }

    /// <summary>
    /// The refusal body is composed by a service this process does not own, and so is the reason phrase beside its status
    /// line; the analyzer's own answer to a rejected request quotes what it was asked.
    /// </summary>
    [Fact]
    public async Task VerifyAvailableAsync_AnalyzerRefusingTheProbe_QuotesNothingTheAnswerCarried()
    {
        // Arrange
        using var context = AnalyzerAnswering(
            """{"error":"a body nobody here composed"}""",
            status: HttpStatusCode.InternalServerError,
            reasonPhrase: "a phrase nobody here composed");

        // Act
        var failure = await Assert.ThrowsAsync<PersonalDataAnalyzerUnavailableException>(
            () => context.Probe.VerifyAvailableAsync(CancellationToken.None));

        // Assert
        Assert.DoesNotContain("nobody here composed", failure.Message, StringComparison.Ordinal);

        // What it reports instead is the status twice over, both renderings this process owns.
        Assert.Contains("500 InternalServerError", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAvailableAsync_AnalyzerThatCannotBeReached_RefusesToStartNamingTheConfigurationKey()
    {
        // Arrange
        using var context = AnalyzerFailingWith(new HttpRequestException("connection refused"));

        // Act
        var failure = await Assert.ThrowsAsync<PersonalDataAnalyzerUnavailableException>(
            () => context.Probe.VerifyAvailableAsync(CancellationToken.None));

        // Assert
        Assert.Contains("SensitiveContent:PersonalDataAnalyzer:Endpoint", failure.Message, StringComparison.Ordinal);
        Assert.IsType<HttpRequestException>(failure.InnerException);
    }

    /// <summary>
    /// A message is what reaches a log, so none of the three refusals carries the analyzer's host name. The address stays
    /// on the failure's own property, which a caller can put somewhere a log line cannot.
    /// </summary>
    [Fact]
    public async Task VerifyAvailableAsync_EveryRefusal_KeepsTheAnalyzerAddressOutOfTheMessage()
    {
        // Arrange
        var narrowedPlan = PersonalDataScanningPlans.For(
            [PersonalDataScanningPlans.Category("PaymentCard"), PersonalDataScanningPlans.Category("HealthIdentifier")]);

        using var unreachable = AnalyzerFailingWith(new HttpRequestException("connection refused"));
        using var refusing = AnalyzerAnswering("""{"error":"refused"}""", status: HttpStatusCode.InternalServerError);
        using var recognisingNothingOfACategory = AnalyzerAnswering("""["CREDIT_CARD"]""", narrowedPlan);

        // Act
        var failures = await Task.WhenAll(
            new[] { unreachable, refusing, recognisingNothingOfACategory }.Select(
                context => Assert.ThrowsAsync<PersonalDataAnalyzerUnavailableException>(
                    () => context.Probe.VerifyAvailableAsync(CancellationToken.None))));

        // Assert
        Assert.All(
            failures,
            failure =>
            {
                Assert.DoesNotContain("analyzer.invalid", failure.Message, StringComparison.Ordinal);
                Assert.Contains(
                    "SensitiveContent:PersonalDataAnalyzer:Endpoint",
                    failure.Message,
                    StringComparison.Ordinal);
                Assert.Equal("http://analyzer.invalid:3000/", failure.Endpoint);
            });
    }

    /// <summary>An address that answers an empty list is something other than a configured analyzer, whatever its status was.</summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task VerifyAvailableAsync_AnalyzerRecognisingNothingAtAll_RefusesToStart(string body)
    {
        // Arrange
        using var context = AnalyzerAnswering(body);

        // Act
        var failure = await Assert.ThrowsAsync<PersonalDataAnalyzerUnavailableException>(
            () => context.Probe.VerifyAvailableAsync(CancellationToken.None));

        // Assert
        Assert.Contains("recognises no entity at all in 'en'", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Each configured language is asked separately, because one answer says nothing about the others.</summary>
    [Fact]
    public async Task VerifyAvailableAsync_SeveralConfiguredLanguages_AsksTheAnalyzerOncePerLanguage()
    {
        // Arrange
        var supported = SupportedEntitiesOf(PersonalDataScanningPlans.Default);
        using var context = MultilingualAnalyzerAnswering(plan: null, supported, supported);

        // Act
        await context.Probe.VerifyAvailableAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            [
                "http://analyzer.invalid:3000/supportedentities?language=en",
                "http://analyzer.invalid:3000/supportedentities?language=pl",
            ],
            context.Handler.RecordedRequests.Select(recorded => recorded.RequestUri?.ToString()));
    }

    /// <summary>
    /// Widening protection must never read as breaking it. A category one configured language reaches is a category this
    /// deployment can find, whatever the others know — otherwise adding a language turns a ready deployment unready.
    /// </summary>
    [Fact]
    public async Task VerifyAvailableAsync_EachCategoryReachableInADifferentLanguage_Passes()
    {
        // Arrange
        // Neither answer satisfies both categories on its own, so a probe that read one language and stopped would fail
        // this rather than pass it — which is what makes the union the thing under test.
        var plan = PersonalDataScanningPlans.For(
            [PersonalDataScanningPlans.Category("NationalIdentifier"), PersonalDataScanningPlans.Category("HealthIdentifier")]);
        using var context = MultilingualAnalyzerAnswering(plan, """["UK_NHS"]""", """["PL_PESEL"]""");

        // Act
        await context.Probe.VerifyAvailableAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, context.Handler.RecordedRequests.Count);
    }

    /// <summary>A category no configured language reaches is still the quiet failure the probe exists to refuse.</summary>
    [Fact]
    public async Task VerifyAvailableAsync_CategoryNoConfiguredLanguageReaches_RefusesToStart()
    {
        // Arrange
        var plan = PersonalDataScanningPlans.For(
            [PersonalDataScanningPlans.Category("NationalIdentifier"), PersonalDataScanningPlans.Category("HealthIdentifier")]);
        using var context = MultilingualAnalyzerAnswering(plan, """["US_SSN"]""", """["PL_PESEL"]""");

        // Act
        var failure = await Assert.ThrowsAsync<PersonalDataAnalyzerUnavailableException>(
            () => context.Probe.VerifyAvailableAsync(CancellationToken.None));

        // Assert
        Assert.Contains("HealthIdentifier", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A language the analyzer was never built for is an operator asking for protection that does not exist, so it is
    /// refused by name rather than absorbed into what another language answered.
    /// </summary>
    [Fact]
    public async Task VerifyAvailableAsync_OneConfiguredLanguageTheAnalyzerKnowsNothingIn_RefusesToStartNamingIt()
    {
        // Arrange
        var supported = SupportedEntitiesOf(PersonalDataScanningPlans.Default);
        using var context = MultilingualAnalyzerAnswering(plan: null, supported, "[]");

        // Act
        var failure = await Assert.ThrowsAsync<PersonalDataAnalyzerUnavailableException>(
            () => context.Probe.VerifyAvailableAsync(CancellationToken.None));

        // Assert
        Assert.Contains("recognises no entity at all in 'pl'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("SensitiveContent:PersonalDataAnalyzer:Languages", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A scrape asks once per configured language, so the budget covers the scrape rather than each request. Without one
    /// a slow analyzer would hold a readiness scrape for a multiple of what an operator configured, which flaps an
    /// instance in and out of traffic instead of reporting what it found.
    /// </summary>
    [Fact]
    public async Task VerifyAvailableAsync_AnalyzerSlowerThanTheScanBudget_RefusesToStartNamingTheBudget()
    {
        // Arrange
        var reachedTheAnalyzer = new TaskCompletionSource();
        using var context = AnalyzerHanging(reachedTheAnalyzer);

        // Act
        var verifying = context.Probe.VerifyAvailableAsync(CancellationToken.None);
        await reachedTheAnalyzer.Task;
        context.TimeProvider.Advance(SensitiveContentScanBounds.Default.ScanTimeout);

        var failure = await Assert.ThrowsAsync<PersonalDataAnalyzerUnavailableException>(() => verifying);

        // Assert
        Assert.Contains("did not answer", failure.Message, StringComparison.Ordinal);
        Assert.Contains("SensitiveContent:ScanTimeout", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("analyzer.invalid", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A host shutting down is its own fact and must not be reported as an analyzer that is missing.</summary>
    [Fact]
    public async Task VerifyAvailableAsync_CancelledCaller_ReportsCancellation()
    {
        // Arrange
        using var context = AnalyzerAnswering("[]");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act and Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.Probe.VerifyAvailableAsync(cancelled.Token));
    }

    private static string SupportedEntitiesOf(SensitiveContentPlan plan) =>
        $"[{string.Join(',', PresidioEntityCorpus.RequestedRules(plan).Keys.Select(entity => $"\"{entity}\""))}]";

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Each object is handed to the returned context, whose Dispose releases all of them; disposing them here would return a context of closed resources.")]
    private static ProbeContext AnalyzerAnswering(
        string body,
        SensitiveContentPlan? plan = null,
        HttpStatusCode status = HttpStatusCode.OK,
        string? reasonPhrase = null)
    {
        var handler = FakeHttpMessageHandler.AlwaysResponding(() => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            ReasonPhrase = reasonPhrase,
        });

        return Context(handler, plan, profile: null);
    }

    /// <summary>An analyzer that answers each language's probe with a registry of its own, in the order they are asked.</summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Each object is handed to the returned context, whose Dispose releases all of them; disposing them here would return a context of closed resources.")]
    private static ProbeContext MultilingualAnalyzerAnswering(SensitiveContentPlan? plan, params string[] bodies)
    {
        var answered = 0;
        var handler = FakeHttpMessageHandler.AlwaysResponding(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(bodies[answered++], Encoding.UTF8, "application/json"),
        });

        return Context(handler, plan, PersonalDataScanningPlans.MultilingualProfile);
    }

    /// <summary>An analyzer that accepts the request and never answers, so only the scrape's own budget ends the wait.</summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Each object is handed to the returned context, whose Dispose releases all of them; disposing them here would return a context of closed resources.")]
    private static ProbeContext AnalyzerHanging(TaskCompletionSource reachedTheAnalyzer)
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            // Signalled before the wait, so the test advances a clock the probe is already timing against rather than
            // one whose budget has not been armed yet.
            reachedTheAnalyzer.TrySetResult();

            await Task.Delay(Timeout.Infinite, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        return Context(handler, plan: null, profile: null);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Each object is handed to the returned context, whose Dispose releases all of them; disposing them here would return a context of closed resources.")]
    private static ProbeContext AnalyzerFailingWith(Exception failure)
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw failure);

        return Context(handler, plan: null, profile: null);
    }

    private static ProbeContext Context(
        FakeHttpMessageHandler handler,
        SensitiveContentPlan? plan,
        PersonalDataAnalyzerProfile? profile)
    {
        var transportFactory = Substitute.For<IHttpClientFactory>();
        transportFactory.CreateClient(PersonalDataAnalyzerProfile.TransportName)
            .Returns(_ => new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = PersonalDataScanningPlans.Profile.Endpoint,
            });

        var timeProvider = new FakeTimeProvider();

        var probe = new PresidioAnalyzerProbe(
            plan ?? PersonalDataScanningPlans.Default,
            profile ?? PersonalDataScanningPlans.Profile,
            transportFactory,
            timeProvider);

        return new ProbeContext(handler, probe, timeProvider);
    }

    private sealed record ProbeContext(
        FakeHttpMessageHandler Handler,
        PresidioAnalyzerProbe Probe,
        FakeTimeProvider TimeProvider) : IDisposable
    {
        public void Dispose() => this.Handler.Dispose();
    }
}
