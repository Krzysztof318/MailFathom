// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Infrastructure.SensitiveContent;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.SensitiveContent;

/// <summary>Covers what the in-process secret detector finds, what it refuses to find, and what it reports about it.</summary>
public sealed class SecretContentScannerTests
{
    private static readonly DateTimeOffset ScannedAt = new(2026, 8, 12, 9, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider timeProvider = new(ScannedAt);

    /// <summary>Each case is the text a mailbox would carry, and the part of it a placeholder has to replace.</summary>
    /// <remarks>
    /// The two are the same for a credential that is the whole of what was written. They differ where the surrounding
    /// text is worth keeping: a connection string still says which database it reached and a link still says what it
    /// linked to, so only the credential inside each is covered.
    /// </remarks>
    public static TheoryData<string, string, string> NamedCategoryPositives() => new()
    {
        { "ProviderToken", SyntheticSecrets.ProviderToken, SyntheticSecrets.ProviderToken },
        { "CloudAccessKey", SyntheticSecrets.CloudAccessKey, SyntheticSecrets.CloudAccessKey },
        { "PrivateKey", SyntheticSecrets.PrivateKey, SyntheticSecrets.PrivateKey },
        { "JsonWebToken", SyntheticSecrets.JsonWebToken, SyntheticSecrets.JsonWebToken },
        { "ConnectionString", SyntheticSecrets.ConnectionString, SyntheticSecrets.ConnectionStringCredential },
        { "CredentialUrl", SyntheticSecrets.CredentialUrl, SyntheticSecrets.CredentialUrlToken },
    };

    [Theory]
    [MemberData(nameof(NamedCategoryPositives))]
    public async Task ScanAsync_ACredentialOfADefaultCategory_IsFoundWhereItSits(
        string category,
        string written,
        string credential)
    {
        // Arrange
        using var scanner = this.Scanner();
        var text = "Here it is: " + written + " — please rotate it.";

        // Act
        var findings = await scanner.ScanAsync(text, TestContext.Current.CancellationToken);

        // Assert
        var finding = Assert.Single(findings, candidate => candidate.Category.Name == category);
        Assert.Equal(credential, text.Substring(finding.Span.Start, finding.Span.Length));
    }

    /// <summary>A mailbox is prose, and a rule that reported prose would cost every other rule its credibility.</summary>
    [Fact]
    public async Task ScanAsync_TextAMailboxIsFullOf_ReportsNothing()
    {
        // Arrange
        using var scanner = this.Scanner();

        // Act
        var findings = await Task.WhenAll(SyntheticSecrets.FalsePositives.Select(async line =>
            new { Line = line, Findings = await scanner.ScanAsync(line, TestContext.Current.CancellationToken) }));

        // Assert
        Assert.Empty(findings
            .Where(result => result.Findings.Count > 0)
            .Select(result => $"{result.Line} => {string.Join(", ", result.Findings.Select(finding => finding.Rule))}"));
    }

    /// <summary>An operator diagnosing a false positive reads the rule off the finding, so it names one and never the value.</summary>
    [Fact]
    public async Task ScanAsync_AFinding_NamesItsRuleAndCorpusRevisionAndNeverTheValue()
    {
        // Arrange
        using var scanner = this.Scanner();

        // Act
        var findings = await scanner.ScanAsync("token " + SyntheticSecrets.ProviderToken, TestContext.Current.CancellationToken);

        // Assert
        var finding = Assert.Single(findings);
        Assert.Equal("github-pat", finding.Rule.Name);
        Assert.Equal("ProviderToken", finding.Category.Name);
        Assert.Equal(SecretRuleCorpus.Detector, finding.Detector);
        Assert.Contains("gitleaks.", finding.Detector.Revision, StringComparison.Ordinal);
        Assert.Equal(1, finding.Confidence);
        Assert.Equal(ScannedAt, finding.DetectedAt);
    }

    /// <summary>One noisy corpus entry must not cost a deployment the category around it.</summary>
    [Fact]
    public async Task ScanAsync_ASuppressedRule_ReportsNothingWhileItsCategoryStillFinds()
    {
        // Arrange
        var providerToken = SecretCategories.ProviderToken;
        using var scanner = this.Scanner(
            [providerToken],
            [SensitiveContentRule.Create(providerToken, "github-pat")]);

        // Act
        var suppressed = await scanner.ScanAsync(SyntheticSecrets.ProviderToken, TestContext.Current.CancellationToken);
        var stillFound = await scanner.ScanAsync(
            "gitlab " + "glpat-" + new string('f', 20),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(suppressed);
        Assert.Equal("gitlab-pat", Assert.Single(stillFound).Rule.Name);
    }

    /// <summary>The entropy layer is the one category an operator opts into, because it is the one that reports ordinary data.</summary>
    [Fact]
    public async Task ScanAsync_AnEntropicRun_IsReportedOnlyWhereThatCategoryIsNamed()
    {
        // Arrange
        var text = "key material " + SyntheticSecrets.HighEntropyString + " ends here";
        using var byDefault = this.Scanner();
        using var withEntropy = this.Scanner([.. SecretCategories.All]);

        // Act
        var ignored = await byDefault.ScanAsync(text, TestContext.Current.CancellationToken);
        var reported = await withEntropy.ScanAsync(text, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(ignored);
        var finding = Assert.Single(reported);
        Assert.Equal("HighEntropyString", finding.Category.Name);
        Assert.InRange(finding.Confidence, 0.5, 1);
    }

    /// <summary>The recall layer still has to tell a credential from a run of two characters, or it reports every attachment.</summary>
    /// <remarks>
    /// The run below is the exact shape one entropy rule looks for — thirty-two base64 bytes — and carries none of the
    /// randomness that makes such a run a credential, so it is the measurement rather than the shape that rejects it.
    /// </remarks>
    [Fact]
    public async Task ScanAsync_ARunOfTheRightShapeAndNoRandomness_IsNotReportedByTheEntropyLayer()
    {
        // Arrange
        using var scanner = this.Scanner([.. SecretCategories.All]);
        var repetitive = string.Concat(Enumerable.Repeat("Ab", 21)) + "C=";

        // Act
        var findings = await scanner.ScanAsync(
            "attachment " + repetitive + " ends here",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(44, repetitive.Length);
        Assert.Empty(findings);
    }

    /// <summary>The engine names a match after the product whose signature it carries, so a name may arrive that no category holds.</summary>
    [Fact]
    public void Resolve_ANameTheCorpusDoesNotDeclare_IsReportedRatherThanLost()
    {
        // Arrange
        using var scanner = this.Scanner();

        // Act
        var (rule, confidence) = scanner.Resolve("AProductThisBuildDoesNotName");

        // Assert
        Assert.Equal(SecretRuleCorpus.UnnamedProviderCredential, rule);
        Assert.Equal(1, confidence);
    }

    /// <summary>A refinement must not resurrect a rule an operator switched off, which is what a name lookup alone would do.</summary>
    [Theory]
    [InlineData("github-pat")]
    [InlineData("Unclassified32ByteBase64String")]
    public void Resolve_ANameTheCorpusDeclaresAndThisDeploymentDidNotRegister_IsDropped(string rule)
    {
        // Arrange
        var providerToken = SecretCategories.ProviderToken;
        using var scanner = this.Scanner(
            [providerToken],
            [SensitiveContentRule.Create(providerToken, "github-pat")]);

        // Act, Assert
        Assert.Null(scanner.Resolve(rule).Rule);
    }

    /// <summary>Suppressing the rule a refinement falls back to has to silence the refinement as well.</summary>
    [Fact]
    public void Resolve_TheUnnamedRuleSuppressed_DropsARefinementInsteadOfReportingIt()
    {
        // Arrange
        using var scanner = this.Scanner(
            [SecretCategories.ProviderToken],
            [SecretRuleCorpus.UnnamedProviderCredential]);

        // Act, Assert
        Assert.Null(scanner.Resolve("AProductThisBuildDoesNotName").Rule);
    }

    /// <summary>The port says a scanner that could not establish what the text carries refuses rather than reports nothing.</summary>
    [Fact]
    public async Task ScanAsync_ADetectionThatCannotBeCompleted_RefusesTheOperation()
    {
        // Arrange
        var broken = Substitute.For<TimeProvider>();
        broken.GetUtcNow().Returns(_ => throw new InvalidOperationException("the clock is unavailable"));
        using var scanner = new SecretContentScanner(
            SensitiveContentPlan.Create(
                SensitiveContentScanBounds.Default,
                [
                    SensitiveContentScannerPlan.Create(
                        SensitiveContentScannerKind.Secrets,
                        [SecretCategories.ProviderToken],
                        []),
                ]),
            broken);

        // Act, Assert
        var failure = await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(
            () => scanner.ScanAsync(SyntheticSecrets.ProviderToken, TestContext.Current.CancellationToken));
        Assert.Equal(SensitiveContentScannerKind.Secrets, failure.Scanner);
    }

    /// <summary>Untrusted text is what this scanner exists for, so input written to make a matcher fall over is part of the contract.</summary>
    [Theory]
    [InlineData("-----BEGIN PRIVATE KEY-----\n")]
    [InlineData("ghp_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ")]
    [InlineData("aaaaaaaaaaaaaa.aaaaa.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-")]
    [InlineData("QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVowMTIzNDU2Nzg5")]
    public async Task ScanAsync_InputBuiltToProvokeAMatcher_StillAnswers(string unit)
    {
        // Arrange
        using var scanner = this.Scanner([.. SecretCategories.All]);
        var hostile = string.Concat(Enumerable.Repeat(unit, 200_000 / unit.Length));

        // Act
        var findings = await scanner.ScanAsync(hostile, TestContext.Current.CancellationToken);

        // Assert
        Assert.All(findings, finding => Assert.InRange(finding.Span.End, 1, hostile.Length));
    }

    [Fact]
    public async Task ScanAsync_ACancelledCaller_IsRefusedRatherThanScanned()
    {
        // Arrange
        using var scanner = this.Scanner();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scanner.ScanAsync(SyntheticSecrets.ProviderToken, cancellation.Token));
    }

    [Fact]
    public void Construct_APlanThatDoesNotSwitchTheSecretScannerOn_IsRejected()
    {
        // Arrange
        var plan = SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [
                SensitiveContentScannerPlan.Create(
                    SensitiveContentScannerKind.Pii,
                    [SensitiveContentCategory.Create("PersonName")],
                    []),
            ]);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => new SecretContentScanner(plan, this.timeProvider));
    }

    /// <summary>Redaction has to be byte-identical on repeat, which starts with the same text producing the same findings.</summary>
    [Fact]
    public async Task ScanAsync_TheSameTextTwice_ProducesTheSameFindings()
    {
        // Arrange
        using var scanner = this.Scanner([.. SecretCategories.All]);
        var text = string.Join(
            "\n",
            SyntheticSecrets.ProviderToken,
            SyntheticSecrets.CloudAccessKey,
            SyntheticSecrets.ConnectionString,
            SyntheticSecrets.CredentialUrl);

        // Act
        var first = await scanner.ScanAsync(text, TestContext.Current.CancellationToken);
        var second = await scanner.ScanAsync(text, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Describe(first), Describe(second));
    }

    private static IEnumerable<string> Describe(IEnumerable<SensitiveContentFinding> findings) =>
        findings.Select(finding => string.Format(
            CultureInfo.InvariantCulture,
            "{0}@{1}+{2}",
            finding.Rule,
            finding.Span.Start,
            finding.Span.Length));

    private SecretContentScanner Scanner(
        IReadOnlyList<SensitiveContentCategory>? categories = null,
        IReadOnlyList<SensitiveContentRule>? suppressions = null) => new(
        SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [
                SensitiveContentScannerPlan.Create(
                    SensitiveContentScannerKind.Secrets,
                    categories ?? [.. SecretCategories.All.Where(SecretCategories.IsDetectedByDefault)],
                    suppressions ?? []),
            ]),
        this.timeProvider);
}
