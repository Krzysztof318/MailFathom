// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Infrastructure.SensitiveContent.Secrets;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.SensitiveContent.Secrets;

/// <summary>Covers what the in-process secret detector finds, what it refuses to find, and what it reports about it.</summary>
public sealed class SecretContentScannerTests
{
    private static readonly DateTimeOffset ScannedAt = new(2026, 8, 12, 9, 30, 0, TimeSpan.Zero);

    /// <summary>Every way a mailbox writes a value, as the text before it and the text after it.</summary>
    /// <remarks>
    /// A rule recognises a credential by its shape and then has to establish where that shape ends, so what follows
    /// the value is half of what it is asked. Source control answers that with a quotation mark or a newline; prose
    /// answers it with a full stop, a comma, a bracket, or the bar a client renders a table cell as. A corpus carried
    /// across from the first reads the second as text carrying no credential at all rather than as a shorter one,
    /// which is the defect these rows exist to keep out. Nothing about a category is under test here — what is under
    /// test is that the answer is the same in all six.
    /// </remarks>
    private static readonly (string Before, string After)[] Surroundings =
    [
        (" ", " and please rotate it."),
        (" ", "."),
        (" ", ","),
        (" (", ") — the one from yesterday."),
        ("\n|", "|\n"),
        ("Here it is: ", string.Empty),
    ];

    private static readonly SecretPositive[] Positives =
    [
        new("ProviderToken", SyntheticSecrets.ProviderToken, SyntheticSecrets.ProviderToken),
        new("CloudAccessKey", SyntheticSecrets.CloudAccessKey, SyntheticSecrets.CloudAccessKey),
        new("PrivateKey", SyntheticSecrets.PrivateKey, SyntheticSecrets.PrivateKey),
        new("JsonWebToken", SyntheticSecrets.JsonWebToken, SyntheticSecrets.JsonWebToken),
        new("ConnectionString", SyntheticSecrets.ConnectionString, SyntheticSecrets.ConnectionStringCredential),
        new("CredentialUrl", SyntheticSecrets.CredentialUrl, SyntheticSecrets.CredentialUrlToken),

        // Five whose rules were the ones ending in the source-control delimiter, so the surroundings above are the
        // whole point of their being here. Each ends in a differently shaped lookahead — hexadecimal, an alphanumeric
        // run, a case-insensitive class, a class carrying the full stop, and a word character — because one shape
        // passing says nothing about the other four.
        new("ProviderToken", SyntheticSecrets.HostingProviderToken, SyntheticSecrets.HostingProviderToken),
        new("ProviderToken", SyntheticSecrets.PaymentPlatformKey, SyntheticSecrets.PaymentPlatformKey),
        new("ProviderToken", SyntheticSecrets.PackageRegistryToken, SyntheticSecrets.PackageRegistryToken),
        new("ProviderToken", SyntheticSecrets.MailPlatformKey, SyntheticSecrets.MailPlatformKey),
        new("CloudAccessKey", SyntheticSecrets.CloudServiceKey, SyntheticSecrets.CloudServiceKey),

        // Both branches of each rule that alternates between credentials of different shapes. One lookahead shared
        // across such a rule forbids the union of its branches' alphabets, which drops a token whose own alphabet had
        // already ended — so a row here per branch is what tells the two apart. One passing says nothing about the
        // other, which is the whole reason they are separate rows rather than one.
        new("ProviderToken", SyntheticSecrets.EdgePlatformToken, SyntheticSecrets.EdgePlatformToken),
        new("ProviderToken", SyntheticSecrets.EdgePlatformBase64Token, SyntheticSecrets.EdgePlatformBase64Token),
        new("ProviderToken", SyntheticSecrets.ModelProviderKey, SyntheticSecrets.ModelProviderKey),
        new("ProviderToken", SyntheticSecrets.ModelProviderProjectKey, SyntheticSecrets.ModelProviderProjectKey),
        new(
            "ProviderToken",
            SyntheticSecrets.ModelProviderProjectKeyEndingInAHyphen,
            SyntheticSecrets.ModelProviderProjectKeyEndingInAHyphen),
        new("ProviderToken", SyntheticSecrets.SecretStoreToken, SyntheticSecrets.SecretStoreToken),
        new("ProviderToken", SyntheticSecrets.SecretStoreLegacyToken, SyntheticSecrets.SecretStoreLegacyToken),

        // The three whose own alphabet carries the full stop. A value ending a sentence is matched one character long,
        // because the quantifier takes the stop before the lookahead is reached, and the surrounding sweep asserts a
        // region covering the credential rather than equalling it for exactly this reason.
        new("ProviderToken", SyntheticSecrets.DatabasePlatformApiToken, SyntheticSecrets.DatabasePlatformApiToken),
        new("ProviderToken", SyntheticSecrets.DatabasePlatformOauthToken, SyntheticSecrets.DatabasePlatformOauthToken),
        new("ProviderToken", SyntheticSecrets.DatabasePlatformPassword, SyntheticSecrets.DatabasePlatformPassword),
    ];

    private readonly FakeTimeProvider timeProvider = new(ScannedAt);

    /// <summary>Each case is the text a mailbox would carry, and the part of it a placeholder has to replace.</summary>
    /// <remarks>
    /// The two are the same for a credential that is the whole of what was written. They differ where the surrounding
    /// text is worth keeping: a connection string still says which database it reached and a link still says what it
    /// linked to, so only the credential inside each is covered.
    /// </remarks>
    public static TheoryData<string, string, string> NamedCategoryPositives()
    {
        var cases = new TheoryData<string, string, string>();

        foreach (var positive in Positives)
        {
            cases.Add(positive.Category, positive.Written, positive.Credential);
        }

        return cases;
    }

    /// <summary>Every default-category credential, written every way a mailbox writes one.</summary>
    public static TheoryData<string, string, string, string, string> EveryPositiveInEverySurrounding()
    {
        var cases = new TheoryData<string, string, string, string, string>();

        foreach (var positive in Positives)
        {
            foreach (var (before, after) in Surroundings)
            {
                cases.Add(positive.Category, positive.Written, positive.Credential, before, after);
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(NamedCategoryPositives))]
    public async Task ScanAsync_ACredentialOfADefaultCategory_IsFoundWhereItSits(
        string category,
        string written,
        string credential)
    {
        // Arrange
        var scanner = this.Scanner();
        var text = "Here it is: " + written + " — please rotate it.";

        // Act
        var findings = await scanner.ScanAsync(text, TestContext.Current.CancellationToken);

        // Assert
        // Asserted over the distinct regions rather than over a single finding, because two of the three corpora
        // recognise some of the same credentials — an npm token is named by the gitleaks rule and by the detection
        // engine's own — and both report the identical span. The redactor merges overlapping regions into one
        // placeholder, so that is a correct state; what would be a defect is a region that is not the credential.
        var regions = Regions(findings, text, category);

        Assert.Equal([credential], regions);
    }

    [Theory]
    [MemberData(nameof(EveryPositiveInEverySurrounding))]
    public async Task ScanAsync_ACredentialOfADefaultCategory_IsFoundWhateverFollowsIt(
        string category,
        string written,
        string credential,
        string before,
        string after)
    {
        // Arrange
        var scanner = this.Scanner();
        var text = before + written + after;

        // Act
        var findings = await scanner.ScanAsync(text, TestContext.Current.CancellationToken);

        // Assert
        // A region has to cover the credential rather than equal it here. Where a rule's own alphabet holds the
        // character that follows — a link's query token and a full stop, say — the greedy quantifier takes that
        // character into the match and one more character is redacted than had to be. That is the direction this
        // feature errs in deliberately: a reader loses a full stop, and nobody loses the credential.
        var covering = Regions(findings, text, category)
            .Where(region => region.Contains(credential, StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(covering);
    }

    /// <summary>Each branch of an alternating rule ends where its own alphabet does, not where its siblings' do.</summary>
    /// <remarks>
    /// The character after each value below belongs to another branch of the same rule and not to the branch that
    /// matched it. A rule closing all of its branches with one shared lookahead forbids their union, so every one of
    /// these reads as text carrying no credential at all — which is why the six surroundings above cannot stand in for
    /// this test: a full stop and a bracket are outside every branch's alphabet and pass either way.
    /// </remarks>
    [Theory]
    [InlineData(nameof(SyntheticSecrets.EdgePlatformToken), '+')]
    [InlineData(nameof(SyntheticSecrets.EdgePlatformToken), '=')]
    [InlineData(nameof(SyntheticSecrets.EdgePlatformBase64Token), '-')]
    [InlineData(nameof(SyntheticSecrets.EdgePlatformBase64Token), '_')]
    [InlineData(nameof(SyntheticSecrets.ModelProviderKey), '-')]
    [InlineData(nameof(SyntheticSecrets.ModelProviderKey), '_')]
    [InlineData(nameof(SyntheticSecrets.SecretStoreLegacyToken), '-')]
    [InlineData(nameof(SyntheticSecrets.SecretStoreLegacyToken), '_')]
    public async Task ScanAsync_ACredentialFollowedByTheAlphabetOfASiblingBranch_IsStillFound(
        string credentialName,
        char following)
    {
        // Arrange
        var scanner = this.Scanner();
        var credential = credentialName switch
        {
            nameof(SyntheticSecrets.EdgePlatformToken) => SyntheticSecrets.EdgePlatformToken,
            nameof(SyntheticSecrets.EdgePlatformBase64Token) => SyntheticSecrets.EdgePlatformBase64Token,
            nameof(SyntheticSecrets.ModelProviderKey) => SyntheticSecrets.ModelProviderKey,
            nameof(SyntheticSecrets.SecretStoreLegacyToken) => SyntheticSecrets.SecretStoreLegacyToken,
            _ => throw new ArgumentOutOfRangeException(nameof(credentialName), credentialName, "No such credential."),
        };

        var text = "The token is " + credential + following + " — rotate it.";

        // Act
        var findings = await scanner.ScanAsync(text, TestContext.Current.CancellationToken);

        // Assert
        var covering = Regions(findings, text, "ProviderToken")
            .Where(region => region.Contains(credential, StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(covering);
    }

    /// <summary>A mailbox is prose, and a rule that reported prose would cost every other rule its credibility.</summary>
    [Fact]
    public async Task ScanAsync_TextAMailboxIsFullOf_ReportsNothing()
    {
        // Arrange
        var scanner = this.Scanner();

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
        var scanner = this.Scanner();

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
        var scanner = this.Scanner(
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
        var byDefault = this.Scanner();
        var withEntropy = this.Scanner([.. SecretCategories.All]);

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
        var scanner = this.Scanner([.. SecretCategories.All]);
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
        var scanner = this.Scanner();

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
        var scanner = this.Scanner(
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
        var scanner = this.Scanner(
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
        var scanner = new SecretContentScanner(
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
        var scanner = this.Scanner([.. SecretCategories.All]);
        var hostile = string.Concat(Enumerable.Repeat(unit, 200_000 / unit.Length));

        // Act
        var findings = await scanner.ScanAsync(hostile, TestContext.Current.CancellationToken);

        // Assert
        Assert.All(findings, finding => Assert.InRange(finding.Span.End, 1, hostile.Length));
    }

    /// <summary>A rule reporting a fraction of what it matched redacts a fraction of the credential, and says nothing about the rest.</summary>
    /// <remarks>
    /// Both cases are ones a rule can pass its own true-positive test while failing at the only job it has. The webhook
    /// URL is matched by an expression whose parentheses repeat a block rather than capture one, and the model-service
    /// key opens with a constant long enough to recognise it by and useless to replace on its own.
    /// </remarks>
    [Theory]
    [InlineData("microsoft-teams-webhook")]
    [InlineData("aws-amazon-bedrock-api-key-short-lived")]
    public async Task ScanAsync_ACredentialWhoseRuleCouldReportPartOfIt_CoversTheWholeCredential(string rule)
    {
        // Arrange
        var scanner = this.Scanner([.. SecretCategories.All]);
        var credential = rule == "microsoft-teams-webhook"
            ? SyntheticSecrets.ChannelWebhookUrl
            : SyntheticSecrets.ShortLivedModelServiceKey;
        var text = "Here it is: " + credential + " — please rotate it.";

        // Act
        var findings = await scanner.ScanAsync(text, TestContext.Current.CancellationToken);

        // Assert
        var finding = Assert.Single(findings, candidate => candidate.Rule.HasName(rule));
        Assert.Equal(credential, text.Substring(finding.Span.Start, finding.Span.Length));
    }

    /// <summary>A budget that bounds only the scans that have not started yet bounds nothing an operator configured it for.</summary>
    /// <remarks>
    /// The clock is read once, after the entry guard and before the first expression runs, so cancelling from it puts
    /// the cancellation exactly where a scan budget's would land: inside a pass already under way. Nothing but the
    /// check between one expression and the next can end it there.
    /// </remarks>
    [Fact]
    public async Task ScanAsync_CancelledOnceThePassHasBegun_EndsItRatherThanRunningEveryExpression()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(_ =>
        {
            cancellation.Cancel();
            return ScannedAt;
        });
        var scanner = new SecretContentScanner(
            SensitiveContentPlan.Create(
                SensitiveContentScanBounds.Default,
                [
                    SensitiveContentScannerPlan.Create(
                        SensitiveContentScannerKind.Secrets,
                        [.. SecretCategories.All],
                        []),
                ]),
            clock);

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scanner.ScanAsync("Here it is: " + SyntheticSecrets.ProviderToken, cancellation.Token));
    }

    [Fact]
    public async Task ScanAsync_ACancelledCaller_IsRefusedRatherThanScanned()
    {
        // Arrange
        var scanner = this.Scanner();
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
        var scanner = this.Scanner([.. SecretCategories.All]);
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

    /// <summary>One credential of a default category: what a mailbox carries, and the part of it a placeholder replaces.</summary>
    private sealed record SecretPositive(string Category, string Written, string Credential);

    /// <summary>The distinct stretches of text the findings of one category cover.</summary>
    private static string[] Regions(
        IReadOnlyList<SensitiveContentFinding> findings,
        string text,
        string category) =>
        [.. findings
            .Where(finding => finding.Category.Name == category)
            .Select(finding => text.Substring(finding.Span.Start, finding.Span.Length))
            .Distinct(StringComparer.Ordinal)];

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
