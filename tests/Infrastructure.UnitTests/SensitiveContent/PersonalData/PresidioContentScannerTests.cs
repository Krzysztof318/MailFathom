// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Infrastructure.SensitiveContent.PersonalData;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.SensitiveContent.PersonalData;

/// <summary>Covers the request the personal-data scanner sends and what it turns each answer into.</summary>
/// <remarks>
/// Everything here is settled by a scripted handler and belongs in this suite for that reason: which entities are asked
/// about, which category an answer is reported under, where a finding lands, and what happens when the analyzer does not
/// answer. What a substitute cannot settle — that the image the deployment pulls answers the request this class builds, in
/// a shape it maps — is <c>OrchestratedPersonalDataAnalyzerTests</c> in the integration suite.
/// </remarks>
public sealed class PresidioContentScannerTests
{
    /// <summary>A fabricated payment card number that satisfies the Luhn checksum and belongs to nobody.</summary>
    private const string SyntheticCardNumber = "4111111111111111";

    private static readonly DateTimeOffset ScannedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The entity list is what decides which categories the analyzer spends work on and which findings can come back.</summary>
    [Fact]
    public async Task ScanAsync_Request_AsksForTheConfiguredEntitiesInTheConfiguredLanguage()
    {
        // Arrange
        using var context = AnalyzerAnswering("[]", PersonalDataScanningPlans.For(
            [PersonalDataScanningPlans.Category("PaymentCard"), PersonalDataScanningPlans.Category("BankAccount")]));

        // Act
        await context.Scanner.ScanAsync($"Card {SyntheticCardNumber}", CancellationToken.None);

        // Assert
        var request = ReadRequest(context.Handler);
        Assert.Equal("en", request.GetProperty("language").GetString());
        Assert.Equal(
            ["CREDIT_CARD", "IBAN_CODE", "US_BANK_NUMBER"],
            request.GetProperty("entities").EnumerateArray().Select(entity => entity.GetString()));
    }

    /// <summary>
    /// The floor is the analyzer's own filter rather than one applied to its answer: left out, it defaults to zero there and
    /// the weakest guesses — a payment card also read as a bank account at 0.05 — arrive as findings redaction acts on.
    /// </summary>
    [Fact]
    public async Task ScanAsync_Request_StatesTheConfiguredConfidenceFloor()
    {
        // Arrange
        using var context = AnalyzerAnswering("[]");

        // Act
        await context.Scanner.ScanAsync($"Card {SyntheticCardNumber}", CancellationToken.None);

        // Assert
        var request = ReadRequest(context.Handler);
        Assert.Equal(
            PersonalDataScanningPlans.MinimumConfidence,
            request.GetProperty("score_threshold").GetDouble());
    }

    [Fact]
    public async Task ScanAsync_Request_ReachesTheAnalyzeRouteOfTheConfiguredAddress()
    {
        // Arrange
        using var context = AnalyzerAnswering("[]");

        // Act
        await context.Scanner.ScanAsync("nothing here", CancellationToken.None);

        // Assert
        var recorded = Assert.Single(context.Handler.RecordedRequests);
        Assert.Equal(HttpMethod.Post, recorded.Method);
        Assert.Equal("http://analyzer.invalid:3000/analyze", recorded.RequestUri?.ToString());
    }

    /// <summary>A finding names the category, points at the region, and carries the profile it was produced under.</summary>
    [Fact]
    public async Task ScanAsync_ReportedEntity_BecomesAFindingOverTheRegionItNamed()
    {
        // Arrange
        var text = $"Card {SyntheticCardNumber} expires soon";
        using var context = AnalyzerAnswering(Reported("CREDIT_CARD", 5, 21, 1.0));

        // Act
        var findings = await context.Scanner.ScanAsync(text, CancellationToken.None);

        // Assert
        var finding = Assert.Single(findings);
        Assert.Equal("PaymentCard", finding.Category.Name);
        Assert.Equal("CREDIT_CARD", finding.Rule.Name);
        Assert.Equal(SyntheticCardNumber, text.Substring(finding.Span.Start, finding.Span.Length));
        Assert.Equal(PersonalDataScanningPlans.Profile.Detector, finding.Detector);
        Assert.Equal(ScannedAt, finding.DetectedAt);
        Assert.Equal(1, finding.Confidence);
    }

    /// <summary>Each default category answers under its own name, which is the name the placeholder a reader sees carries.</summary>
    [Theory]
    [InlineData("CREDIT_CARD", "PaymentCard")]
    [InlineData("IBAN_CODE", "BankAccount")]
    [InlineData("PL_PESEL", "NationalIdentifier")]
    [InlineData("US_SSN", "NationalIdentifier")]
    [InlineData("UK_PASSPORT", "IdentityDocument")]
    [InlineData("US_DRIVER_LICENSE", "IdentityDocument")]
    [InlineData("UK_NHS", "HealthIdentifier")]
    public async Task ScanAsync_EntityOfADefaultCategory_IsReportedUnderThatCategory(string entity, string category)
    {
        // Arrange
        using var context = AnalyzerAnswering(Reported(entity, 0, 8, 0.85));

        // Act
        var findings = await context.Scanner.ScanAsync("12345678 and more text", CancellationToken.None);

        // Assert
        var finding = Assert.Single(findings);
        Assert.Equal(category, finding.Category.Name);
        Assert.Equal(0.85, finding.Confidence);
    }

    /// <summary>
    /// A category the operator left out redacts nothing. The entity is not requested, and an analyzer running recognizers of
    /// its own that reports it anyway is answering a question nobody asked.
    /// </summary>
    [Fact]
    public async Task ScanAsync_EntityOfACategoryLeftOutOfTheConfiguredList_RedactsNothing()
    {
        // Arrange
        using var context = AnalyzerAnswering(
            Reported("PERSON", 0, 5, 0.85),
            PersonalDataScanningPlans.For([PersonalDataScanningPlans.Category("PaymentCard")]));

        // Act
        var findings = await context.Scanner.ScanAsync("Alice sent a message", CancellationToken.None);

        // Assert
        Assert.Empty(findings);
    }

    /// <summary>A suppressed entity is silent inside a category that stays on, and the rest of the category still answers.</summary>
    [Fact]
    public async Task ScanAsync_SuppressedEntity_IsSilentWhileTheCategoryStaysOn()
    {
        // Arrange
        using var context = AnalyzerAnswering(
            $"[{Entity("US_BANK_NUMBER", 0, 8, 0.7)},{Entity("IBAN_CODE", 9, 17, 0.9)}]",
            PersonalDataScanningPlans.For(
                [PersonalDataScanningPlans.Category("BankAccount")],
                [PersonalDataScanningPlans.Rule("BankAccount", "US_BANK_NUMBER")]));

        // Act
        var findings = await context.Scanner.ScanAsync("12345678 PL601020", CancellationToken.None);

        // Assert
        var finding = Assert.Single(findings);
        Assert.Equal("IBAN_CODE", finding.Rule.Name);
    }

    /// <summary>An entity no category maps onto is ignored rather than failing a scan that would then fail every message.</summary>
    [Fact]
    public async Task ScanAsync_EntityTheMappingDoesNotKnow_IsIgnored()
    {
        // Arrange
        using var context = AnalyzerAnswering(
            $"[{Entity("AN_ENTITY_NOBODY_MAPPED", 0, 4, 0.9)},{Entity("CREDIT_CARD", 5, 21, 1.0)}]");

        // Act
        var findings = await context.Scanner.ScanAsync($"Card {SyntheticCardNumber} expires", CancellationToken.None);

        // Assert
        var finding = Assert.Single(findings);
        Assert.Equal("CREDIT_CARD", finding.Rule.Name);
    }

    /// <summary>
    /// The analyzer counts a character outside the basic plane as one position and .NET as two, so a response mapped
    /// verbatim would redact the wrong region and leave part of the value behind.
    /// </summary>
    [Fact]
    public async Task ScanAsync_TextCarryingACharacterOutsideTheBasicPlane_LandsOnTheRegionTheAnalyzerMeant()
    {
        // Arrange
        var text = $"\U0001F4E7 {SyntheticCardNumber}";
        using var context = AnalyzerAnswering(Reported("CREDIT_CARD", 2, 18, 1.0));

        // Act
        var findings = await context.Scanner.ScanAsync(text, CancellationToken.None);

        // Assert
        var finding = Assert.Single(findings);
        Assert.Equal(SyntheticCardNumber, text.Substring(finding.Span.Start, finding.Span.Length));
    }

    /// <summary>An empty text carries nothing, and the analyzer refuses a request with none — which would fail an operation.</summary>
    [Fact]
    public async Task ScanAsync_EmptyText_AsksTheAnalyzerNothing()
    {
        // Arrange
        using var context = AnalyzerAnswering("[]");

        // Act
        var findings = await context.Scanner.ScanAsync(string.Empty, CancellationToken.None);

        // Assert
        Assert.Empty(findings);
        Assert.Empty(context.Handler.RecordedRequests);
    }

    /// <summary>Every way the analyzer fails to answer refuses the operation the scan guards rather than reporting a clean text.</summary>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "[]")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"No text provided"}""")]
    [InlineData(HttpStatusCode.NotFound, "")]
    [InlineData(HttpStatusCode.OK, "not json at all")]
    [InlineData(HttpStatusCode.OK, "null")]
    public async Task ScanAsync_AnalyzerThatDoesNotAnswerUsably_RefusesTheOperation(HttpStatusCode status, string body)
    {
        // Arrange
        using var context = AnalyzerAnswering(body, status: status);

        // Act
        var failure = await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(
            () => context.Scanner.ScanAsync("some text", CancellationToken.None));

        // Assert
        Assert.Equal(SensitiveContentScannerKind.Pii, failure.Scanner);
    }

    /// <summary>An analyzer that cannot be reached at all is the same outcome, and it names no address on a serving path.</summary>
    [Fact]
    public async Task ScanAsync_TransportFailure_RefusesTheOperationWithoutNamingTheAddress()
    {
        // Arrange
        using var context = AnalyzerFailingWith(new HttpRequestException("connection refused"));

        // Act
        var failure = await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(
            () => context.Scanner.ScanAsync("some text", CancellationToken.None));

        // Assert
        Assert.DoesNotContain("analyzer.invalid", failure.Message, StringComparison.Ordinal);
        Assert.IsType<HttpRequestException>(failure.InnerException);
    }

    /// <summary>
    /// A region outside the text this process just sent is a fault rather than a detection. Clamping it would redact
    /// characters nothing was found in while leaving whatever the analyzer meant untouched.
    /// </summary>
    [Theory]
    [InlineData(0, 999)]
    [InlineData(-1, 4)]
    [InlineData(4, 4)]
    public async Task ScanAsync_EntityOutsideTheTextItWasHanded_RefusesTheOperation(int start, int end)
    {
        // Arrange
        using var context = AnalyzerAnswering(Reported("CREDIT_CARD", start, end, 1.0));

        // Act
        var failure = await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(
            () => context.Scanner.ScanAsync("some text", CancellationToken.None));

        // Assert
        Assert.Equal(SensitiveContentScannerKind.Pii, failure.Scanner);
    }

    /// <summary>A caller's cancellation is its own fact and must not be reported as an analyzer that failed.</summary>
    [Fact]
    public async Task ScanAsync_CancelledCaller_ReportsCancellationRatherThanAFailedScanner()
    {
        // Arrange
        using var context = AnalyzerAnswering("[]");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act and Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.Scanner.ScanAsync("some text", cancelled.Token));
    }

    /// <summary>The request body is mail content, so a record that printed its members would put a message body in a log line.</summary>
    [Fact]
    public void PresidioAnalyzeRequest_ToString_CarriesNoText()
    {
        // Arrange
        var request = new PresidioAnalyzeRequest("a mail body", "en", ["CREDIT_CARD"], 0.3);

        // Act
        var formatted = request.ToString();

        // Assert
        Assert.Equal("***", formatted);
    }

    private static string Reported(string entity, int start, int end, double score) =>
        $"[{Entity(entity, start, end, score)}]";

    private static string Entity(string entity, int start, int end, double score) => string.Format(
        CultureInfo.InvariantCulture,
        """{{"analysis_explanation":null,"end":{0},"entity_type":"{1}","score":{2},"start":{3}}}""",
        end,
        entity,
        score,
        start);

    private static JsonElement ReadRequest(FakeHttpMessageHandler handler)
    {
        var recorded = Assert.Single(handler.RecordedRequests);

        return JsonDocument.Parse(recorded.ContentAsUtf8String()).RootElement;
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Each object is handed to the returned context, whose Dispose releases all of them; disposing them here would return a context of closed resources.")]
    private static ScannerContext AnalyzerAnswering(
        string body,
        SensitiveContentPlan? plan = null,
        HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = FakeHttpMessageHandler.AlwaysResponding(() => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

        return Context(handler, plan);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Each object is handed to the returned context, whose Dispose releases all of them; disposing them here would return a context of closed resources.")]
    private static ScannerContext AnalyzerFailingWith(Exception failure)
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw failure);

        return Context(handler, plan: null);
    }

    private static ScannerContext Context(FakeHttpMessageHandler handler, SensitiveContentPlan? plan)
    {
        // A fresh client per call, as the factory hands out: the scanner opens one per scan and disposes it, so a double
        // returning one instance twice would answer the second scan from a disposed client and report a failure the
        // production wiring cannot produce. The handler outlives them all and records what each of them sent.
        var transportFactory = Substitute.For<IHttpClientFactory>();
        transportFactory.CreateClient(PersonalDataAnalyzerProfile.TransportName)
            .Returns(_ => new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = PersonalDataScanningPlans.Profile.Endpoint,
            });

        var scanner = new PresidioContentScanner(
            plan ?? PersonalDataScanningPlans.Default,
            PersonalDataScanningPlans.Profile,
            transportFactory,
            new FakeTimeProvider(ScannedAt));

        return new ScannerContext(handler, scanner);
    }

    private sealed record ScannerContext(FakeHttpMessageHandler Handler, PresidioContentScanner Scanner) : IDisposable
    {
        public void Dispose() => this.Handler.Dispose();
    }
}
