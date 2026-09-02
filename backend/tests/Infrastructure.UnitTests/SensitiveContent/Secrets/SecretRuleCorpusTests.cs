// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;
using MailFathom.Infrastructure.SensitiveContent.Secrets;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.SensitiveContent.Secrets;

/// <summary>Covers the invariants the assembled corpus has to hold whichever of its three sources supplied a rule.</summary>
public sealed class SecretRuleCorpusTests
{
    private static readonly string[] ProvidersAMailboxReceives =
    [
        "github-pat", "gitlab-pat", "slack-bot-token", "openai-api-key", "anthropic-api-key",
        "aws-access-token", "gcp-api-key", "stripe-access-token", "sendgrid-api-token", "npm-access-token",
        "pypi-upload-token", "hashicorp-tf-api-token", "grafana-service-account-token", "shopify-access-token",
    ];

    /// <summary>A suppression names one rule, so two rules answering to one name would silence something unintended.</summary>
    [Fact]
    public void Rules_NameEveryEntryOnce()
    {
        // Act
        var repeated = SecretRuleCorpus.Rules
            .GroupBy(definition => definition.Rule.Name, StringComparer.OrdinalIgnoreCase)
            .Where(sameName => sameName.Count() > 1)
            .Select(sameName => sameName.Key);

        // Assert
        Assert.Empty(repeated);
    }

    [Fact]
    public void Rules_BelongToTheCategoriesThisScannerDeclares()
    {
        // Act
        var stray = SecretRuleCorpus.Rules
            .Where(definition => !SecretCategories.All.Contains(definition.Rule.Category))
            .Select(definition => definition.Rule.ToString());

        // Assert
        Assert.Empty(stray);
    }

    /// <summary>The gap this corpus exists to close is measurable: the engine alone ships two third-party patterns.</summary>
    [Fact]
    public void Rules_CoverTheThirdPartyProvidersAMailboxReceives()
    {
        // Act
        var names = SecretRuleCorpus.Rules.Select(definition => definition.Rule.Name).ToHashSet(StringComparer.Ordinal);

        // Assert
        Assert.All(ProvidersAMailboxReceives, expected => Assert.Contains(expected, names));
    }

    /// <summary>Two deployments redact identically only against a stated corpus, and this one is assembled from three.</summary>
    [Fact]
    public void Detector_NamesTheRevisionOfEveryCorpusItWasAssembledFrom()
    {
        // Act
        var revision = SecretRuleCorpus.Detector.Revision;

        // Assert
        Assert.Equal("mailfathom-secrets", SecretRuleCorpus.Detector.Name);
        Assert.Contains(
            "gitleaks." + GitleaksSecretRules.CorpusRevision + "." + GitleaksSecretRules.TransformationRevision,
            revision,
            StringComparison.Ordinal);
        Assert.Contains("own." + MailFathomSecretRules.CorpusRevision, revision, StringComparison.Ordinal);
    }

    /// <summary>The delimiter alternation gitleaks ends its expressions with never comes back through a refresh.</summary>
    /// <remarks>
    /// <para>
    /// gitleaks closes most of its rules with a group requiring a backtick, a quotation mark, whitespace, a semicolon,
    /// an escaped newline, or the end of the text after the credential, because that is where one ends in source
    /// control. In mail it is where one almost never ends, so a rule carrying that group reports nothing at all for a
    /// credential closing a sentence — and reports nothing in a way no true-positive test written beside it can see,
    /// because such a test writes the value with a space after it.
    /// </para>
    /// <para>
    /// <see cref="GitleaksSecretRules" /> replaces it with a negative lookahead as one of the four transformations its
    /// remarks name, and this is what keeps that transformation applied. The corpus is refreshed by copying expressions
    /// from an upstream release that still ends them the old way, so the alternation arriving back is the ordinary
    /// outcome of a refresh rather than a mistake somebody would notice.
    /// </para>
    /// </remarks>
    [Fact]
    public void Rules_CompiledHere_NeverEndInTheDelimiterGitleaksWroteForSourceControl()
    {
        // Arrange
        const string SourceControlDelimiter = """(?:[\x60'"\s;]|\\[nr]|$)""";

        // Act
        var carried = SecretRuleCorpus.Rules
            .Where(definition => definition.Expression is not null)
            .Where(definition => definition.Expression!.ToString().EndsWith(SourceControlDelimiter, StringComparison.Ordinal))
            .Select(definition => definition.Rule.ToString());

        // Assert
        Assert.Empty(carried);
    }

    /// <summary>Every expression MailFathom compiled is bounded, whichever of the two files it was written in.</summary>
    [Fact]
    public void Rules_CompiledHere_CarryAnExplicitMatchTimeout()
    {
        // Act
        var unbounded = SecretRuleCorpus.Rules
            .Where(definition => definition.Expression is not null)
            .Where(definition => definition.Expression!.MatchTimeout == Regex.InfiniteMatchTimeout)
            .Select(definition => definition.Rule.ToString());

        // Assert
        Assert.Empty(unbounded);
    }

    /// <summary>A repeated group's last capture is what a finding would cover, which is never the whole credential.</summary>
    /// <remarks>
    /// This is the shape of the one defect a true-positive test cannot see: the rule fires, the category is right, the
    /// name is right, and the span covers one repetition of a block inside the credential. Naming a group that a
    /// quantifier then repeats is the only way to write it, so rejecting that shape rejects the defect.
    /// </remarks>
    [Fact]
    public void Rules_CompiledHere_NeverNarrowAFindingToARepeatedGroup()
    {
        // Act
        var repeated = SecretRuleCorpus.Rules
            .Where(definition => definition.Expression is not null)
            .Where(definition => SecretGroupIsRepeated(definition.Expression!.ToString()))
            .Select(definition => definition.Rule.ToString());

        // Assert
        Assert.Empty(repeated);
    }

    /// <summary>Reports whether a quantifier follows the secret group, which makes its last capture the reported one.</summary>
    private static bool SecretGroupIsRepeated(string pattern)
    {
        var opener = "(?<" + SecretRuleDefinition.SecretCaptureGroup + ">";
        var start = pattern.IndexOf(opener, StringComparison.Ordinal);

        if (start < 0)
        {
            return false;
        }

        var depth = 1;
        var index = start + opener.Length;

        while (index < pattern.Length && depth > 0)
        {
            switch (pattern[index])
            {
                case '\\':
                    index++;
                    break;
                case '[':
                    while (index < pattern.Length && pattern[index] != ']')
                    {
                        index += pattern[index] == '\\' ? 2 : 1;
                    }

                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                default:
                    break;
            }

            index++;
        }

        return index < pattern.Length && "*+?{".Contains(pattern[index], StringComparison.Ordinal);
    }

    /// <summary>The engine finds an expression again by the text it was built from, so the two must agree.</summary>
    [Fact]
    public void Rules_CompiledHere_RegisterThePatternTheirMatcherWasBuiltFrom()
    {
        // Act
        var mismatched = SecretRuleCorpus.Rules
            .Where(definition => definition.Expression is not null)
            .Where(definition => !string.Equals(
                definition.Pattern.Pattern,
                definition.Expression!.ToString(),
                StringComparison.Ordinal))
            .Select(definition => definition.Rule.ToString());

        // Assert
        Assert.Empty(mismatched);
    }
}
