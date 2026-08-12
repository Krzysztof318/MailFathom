// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Sockets;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Domain.Failures;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.SensitiveContent.PersonalData;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.SensitiveContent;

/// <summary>Proves the personal-data adapter against the analyzer image a deployment actually pulls.</summary>
/// <remarks>
/// <para>
/// Everything about this scanner that a substitute can settle is settled in the unit suite, where it is cheaper: which
/// category maps to which entity, what a suppression silences, how a refusal is classified, and what an entity the mapping
/// does not know does to the result. What no substitute settles is the claim this class makes — that a real analyzer, on
/// the pinned image, answers the request MailFathom builds with the entities MailFathom named, in a shape the adapter maps
/// back without dropping a finding and over offsets that land on the region of the original text. A handler answering with
/// a payload somebody hand-wrote proves the mapping works on the payload somebody hand-wrote.
/// </para>
/// <para>
/// <b>Every fixture here is fabricated.</b> The inputs are by construction the exact shapes of payment instruments, bank
/// accounts, and identity documents, so each one is a published test value or a value built to satisfy its own checksum and
/// belonging to nobody. That rule bites harder in this class than anywhere else in the repository and it has no exception.
/// </para>
/// <para>
/// The class joins the shared-infrastructure collection although it touches neither the database nor the mailbox. The
/// analyzer is a single container holding a language model, and a scan is the most expensive request the suite makes of it;
/// running these beside the tests that already serialize keeps that cost off whatever else is in flight.
/// </para>
/// <para>
/// Two claims are reached differently from the way the issue describes them, and both are deliberate. An entity the mapping
/// does not know cannot be produced here at all — the analyzer answers strictly within the entity list it was asked for,
/// and that list is built from the mapping — so the ignoring branch stays unit-covered and what this class proves instead
/// is that a category left out of the plan is never asked about. And a failure is reached through an address nothing
/// answers at rather than by stopping the analyzer: the container is shared with the rest of the run, stopping a resource
/// this class did not start would break every test behind it, and the code path is identical either way.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedPersonalDataAnalyzerTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The floor the tests state, low enough that every fixture below is reported and high enough to drop the analyzer's sub-0.1 noise.</summary>
    private const double MinimumConfidence = 0.3;

    /// <summary>
    /// One synthetic value per category the product detects by default, each in the surrounding words the analyzer's
    /// recognizers read as context, with the entity and the exact region the pinned image reports for it.
    /// </summary>
    /// <remarks>
    /// The regions are stated rather than searched for, because a region searched for in the input would pass against an
    /// adapter that had mistranslated the analyzer's offsets by a constant.
    /// </remarks>
    public static TheoryData<string, string, string, string, int, int> DefaultCategoryFixtures => new()
    {
        { "PaymentCard", "CREDIT_CARD", "Payment card 4111111111111111 on file", "4111111111111111", 13, 16 },
        { "BankAccount", "IBAN_CODE", "Account GB82WEST12345698765432 please", "GB82WEST12345698765432", 8, 22 },
        { "NationalIdentifier", "US_ITIN", "ITIN 912-70-1234 filed", "912-70-1234", 5, 11 },
        { "IdentityDocument", "US_DRIVER_LICENSE", "Driver license number D1234567 shown", "D1234567", 22, 8 },
        { "HealthIdentifier", "UK_NHS", "NHS number 943 476 5919 recorded", "943 476 5919", 11, 12 },
    };

    /// <summary>Every category the product hides by default is one the shipped analyzer really finds, in the region it really occupies.</summary>
    [Theory]
    [MemberData(nameof(DefaultCategoryFixtures))]
    public async Task ScanAsync_ASyntheticValueOfADefaultCategory_IsFoundOverTheRegionItOccupies(
        string category,
        string entity,
        string text,
        string expectedValue,
        int expectedStart,
        int expectedLength)
    {
        // Arrange
        await using var composition = this.Compose(PersonalDataScanningPlan.Default);
        var scanner = ScannerOf(composition);

        // Act
        var findings = await scanner.ScanAsync(text, TestContext.Current.CancellationToken);

        // Assert
        var finding = Assert.Single(findings, candidate => candidate.Rule.HasName(entity));
        Assert.True(finding.Category.HasName(category));
        Assert.Equal(expectedStart, finding.Span.Start);
        Assert.Equal(expectedLength, finding.Span.Length);

        // The span is what redaction replaces, so the assertion that matters is what it selects out of the input.
        Assert.Equal(expectedValue, text.Substring(finding.Span.Start, finding.Span.Length));
        Assert.True(finding.Confidence >= MinimumConfidence);
        Assert.Equal(PresidioEntityCorpus.DetectorName, finding.Detector.Name);
    }

    /// <summary>
    /// The analyzer counts a Python string's characters and .NET counts UTF-16 code units, so a message with an emoji in
    /// front of a payment card reports offsets that are correct for neither text until they are translated. Untranslated,
    /// the region would start one character early and end one character early — leaving the last digit of the number in
    /// the redacted text and eating the character in front of it.
    /// </summary>
    [Fact]
    public async Task ScanAsync_TextCarryingACharacterOutsideTheBasicPlane_ReportsTheRegionInUtf16Offsets()
    {
        // Arrange
        const string text = "\U0001F4E7 card 4111111111111111 filed";
        await using var composition = this.Compose(PersonalDataScanningPlan.Default);
        var scanner = ScannerOf(composition);

        // Act
        var findings = await scanner.ScanAsync(text, TestContext.Current.CancellationToken);

        // Assert
        var finding = Assert.Single(findings, candidate => candidate.Rule.HasName("CREDIT_CARD"));
        Assert.Equal("4111111111111111", text.Substring(finding.Span.Start, finding.Span.Length));

        // Stated as well as derived: the analyzer reports this region as 7 to 23 counting code points, and the emoji is
        // what makes those two numbers different from the ones a .NET string is indexed by.
        Assert.Equal(8, finding.Span.Start);
        Assert.Equal(24, finding.Span.End);
    }

    /// <summary>
    /// A category the operator left out is never asked about, which is what keeps the analyzer from spending work on it and
    /// keeps a finding nobody configured from reaching redaction.
    /// </summary>
    [Fact]
    public async Task ScanAsync_ACategoryTheDeploymentLeftOut_FindsNothingInTextThatCarriesIt()
    {
        // Arrange
        await using var composition = this.Compose(
            PersonalDataScanningPlan.For([PersonalDataScanningPlan.Category("HealthIdentifier")]));
        var scanner = ScannerOf(composition);

        // Act
        var findings = await scanner.ScanAsync(
            "Payment card 4111111111111111 on file",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(findings);
    }

    /// <summary>
    /// The probe is what turns an analyzer that recognises nothing MailFathom asks for into a startup failure, so the claim
    /// worth proving here is that the shipped image registers at least one entity behind every category the product
    /// declares — including the ones an operator switches on rather than the defaults alone.
    /// </summary>
    [Fact]
    public async Task VerifyAvailableAsync_TheOrchestratedAnalyzer_RecognizesSomethingForEveryDeclaredCategory()
    {
        // Arrange
        await using var composition = this.Compose(
            PersonalDataScanningPlan.For(PersonalDataScanningPlan.EveryDeclaredCategory()));
        var probe = composition.GetRequiredService<IPersonalDataAnalyzerProbe>();

        // Act
        var probing = probe.VerifyAvailableAsync(TestContext.Current.CancellationToken);

        await probing;

        // Assert
        // Completing is the whole contract: the probe raises the startup failure for a category the analyzer answers
        // nothing for, and reports nothing at all when every one of them is recognised.
        Assert.True(probing.IsCompletedSuccessfully);
    }

    /// <summary>
    /// A crashed sidecar must not read as a clean message. Both halves are asserted together because they are one contract
    /// seen from two sides: startup refuses to come up, and a scan that reaches an absent analyzer refuses the operation it
    /// guards rather than reporting that nothing was found.
    /// </summary>
    [Fact]
    public async Task AgainstAnAnalyzerNothingAnswersAt_BothTheProbeAndAScanFailClosed()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var composition = Compose(PersonalDataScanningPlan.Default, UnreachableAnalyzer());

        // Act
        var startupFailure = await Assert.ThrowsAsync<PersonalDataAnalyzerUnavailableException>(() =>
            composition.GetRequiredService<IPersonalDataAnalyzerProbe>().VerifyAvailableAsync(cancellationToken));
        var scanFailure = await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() =>
            ScannerOf(composition).ScanAsync("Payment card 4111111111111111 on file", cancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.PersonalDataAnalyzerUnavailable, startupFailure.ErrorCode);
        Assert.Equal(SensitiveContentScannerKind.Pii, scanFailure.Scanner);
    }

    private static ISensitiveContentScanner ScannerOf(IServiceProvider composition) =>
        Assert.Single(composition.GetServices<ISensitiveContentScanner>());

    /// <summary>Reserves a loopback port and releases it, so the address is one nothing is listening on.</summary>
    /// <remarks>
    /// Asking the operating system for a free port and giving it back is how a port known to refuse a connection is
    /// obtained; a number written here would be one some other process on the machine might hold. Something else could in
    /// principle take it between the release and the request, and would then have to answer an analyzer's route for this
    /// test to pass rather than fail.
    /// </remarks>
    private static Uri UnreachableAnalyzer()
    {
        using var reservation = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        reservation.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        var port = ((IPEndPoint)reservation.LocalEndPoint!).Port;

        return new Uri($"http://127.0.0.1:{port}", UriKind.Absolute);
    }

    private static ServiceProvider Compose(SensitiveContentPlan plan, Uri analyzer)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(plan);
        services.AddSingleton(PersonalDataAnalyzerProfile.Create(analyzer, "en", MinimumConfidence));

        // The real registration rather than a client composed here, so what these tests exercise includes the bounds and
        // the base-address handling a deployment gets. The standard resilience handler the host's service defaults add is
        // absent, which is the one difference and the harmless one: it would repeat a refused call rather than change what
        // the adapter reports.
        services.AddPersonalDataContentScanning();

        return services.BuildServiceProvider();
    }

    private ServiceProvider Compose(SensitiveContentPlan plan) =>
        Compose(plan, orchestration.PersonalDataAnalyzer);
}
