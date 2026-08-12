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
/// <para>
/// The false-positive corpus is here rather than in the unit suite for the reason the rest of this class is: detection
/// happens in the analyzer, so a substitute asked about prose answers whatever the substitute was written to answer. Its
/// counterpart for the in-process secret scanner is a unit test, because there the detector really is in the process.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedPersonalDataAnalyzerTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The product's own default floor, which is what these fixtures are chosen to sit on either side of.</summary>
    /// <remarks>
    /// Stated as a literal rather than read from the options type, because the two claims this class makes about it are that
    /// a deployment's default finds every category and reports nothing in prose. Reading the value under test out of the
    /// code under test would make both hold at whatever the default became.
    /// </remarks>
    private const double MinimumConfidence = 0.4;

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

        // The second identity-document row is the one that pins the floor from above: the analyzer scores this number at
        // exactly 0.4, so a default raised by any amount stops finding passport numbers and fails here.
        { "IdentityDocument", "US_PASSPORT", "Passport number 912803456 issued", "912803456", 16, 9 },
    };

    /// <summary>Prose a mailbox is full of that carries no identifier of any default category.</summary>
    /// <remarks>
    /// Each line is something the analyzer's own recognizers come close to and score below the default floor: a commit
    /// hash, an invoice reference and a build number it reads as a bank account at 0.05, a contract reference it reads as a
    /// driving licence at 0.3, a hyphenated ticket number shaped like a social security number, a room and an order number,
    /// and a host address. The 0.3 line is why the default is what it is, and this corpus is what fails if that stops being
    /// true — through a floor somebody lowered or an analyzer release that scores a pattern differently.
    /// </remarks>
    private static IReadOnlyList<string> FalsePositiveCorpus { get; } =
    [
        "The fix landed in commit 9f2c1ab7e45d0836ba91cc57de204f6a8b3e1d92, please rebase onto it.",
        "Invoice 2026/08/0142 for 1 240,00 EUR is attached; the reference is INV-20260812-0142.",
        "The build number is 20260812 and the release notes are attached.",
        "Contract A1234567 was signed by both parties last week.",
        "Ticket 123-45-6789 in the tracker is the one about the flaky formatter test.",
        "Session begins at 09:30 CEST in room 4471; the agenda is in the shared folder.",
        "Order 4471 shipped on Tuesday and the courier left it with reception.",
        "The staging environment is at 10.0.0.14 and the database listens on port 5432.",
    ];

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

        // Nothing else is reported either. A second recognizer reading the same span as another category is exactly what
        // the floor holds back — a passport number is also a national identifier at 0.3 — so tolerating extra findings here
        // would let a lowered floor pass.
        Assert.All(findings, candidate => Assert.True(candidate.Category.HasName(category)));

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
    /// A mailbox is prose, and the floor is the only thing keeping the analyzer's weakest patterns out of it. This is the
    /// test that fails if the default stops suppressing them.
    /// </summary>
    /// <remarks>
    /// Asserted over the whole corpus at once rather than one line per case, so a failure names every line that started
    /// reporting something instead of the first one.
    /// </remarks>
    [Fact]
    public async Task ScanAsync_ProseCarryingNoIdentifier_ReportsNothingAtTheDefaultFloor()
    {
        // Arrange
        await using var composition = this.Compose(PersonalDataScanningPlan.Default);
        var scanner = ScannerOf(composition);

        // Act
        var scanned = await Task.WhenAll(FalsePositiveCorpus.Select(async line => new
        {
            Line = line,
            Findings = await scanner.ScanAsync(line, TestContext.Current.CancellationToken),
        }));

        // Assert
        Assert.Empty(scanned
            .Where(result => result.Findings.Count > 0)
            .Select(result => $"{result.Line} => {string.Join(", ", result.Findings.Select(finding => finding.Rule))}"));
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

        // The real registration rather than a client composed here, so what these tests exercise includes the bounds, the
        // base-address handling, and the resilience handler a deployment gets: the registration adds that handler itself,
        // bounded by this plan's scan timeout, so the transport here matches a deployment's rather than differing from it.
        // A refused connection is therefore repeated here too, which is what the unreachable-analyzer test spends its
        // seconds on before either half of the contract reports anything.
        services.AddPersonalDataContentScanning();

        return services.BuildServiceProvider();
    }

    private ServiceProvider Compose(SensitiveContentPlan plan) =>
        Compose(plan, orchestration.PersonalDataAnalyzer);
}
