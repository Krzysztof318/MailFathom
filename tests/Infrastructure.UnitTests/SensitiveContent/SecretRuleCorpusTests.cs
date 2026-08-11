// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;
using MailFathom.Infrastructure.SensitiveContent;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.SensitiveContent;

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
        Assert.Contains("gitleaks." + GitleaksSecretRules.CorpusRevision, revision, StringComparison.Ordinal);
        Assert.Contains("own." + MailFathomSecretRules.CorpusRevision, revision, StringComparison.Ordinal);
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
