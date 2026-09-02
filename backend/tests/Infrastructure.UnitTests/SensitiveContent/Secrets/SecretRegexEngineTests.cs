// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;
using MailFathom.Infrastructure.SensitiveContent.Secrets;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.SensitiveContent.Secrets;

/// <summary>Covers how an expression is found again from the pattern text, and what bounds it once it is.</summary>
public sealed class SecretRegexEngineTests
{
    private readonly SecretRegexEngine engine = new(SecretRuleCorpus.Rules);

    /// <summary>The whole point of the seam: a compile-time matcher reached through an API that takes a string.</summary>
    [Fact]
    public void Matches_APatternTheCorpusCompiled_ReportsTheCaptureRatherThanTheWholeMatch()
    {
        // Arrange
        var rule = SecretRuleCorpus.Rules.Single(definition => definition.Rule.Name == "aws-access-token");
        var text = "key " + "AK" + "IA" + new string('B', 16) + ", rotate it";

        // Act
        var matches = this.engine.Matches(text, rule.Pattern.Pattern, rule.Pattern.RegexOptions, captureGroup: "refine");

        // Assert
        var match = Assert.Single(matches);
        Assert.Equal("AK" + "IA" + new string('B', 16), match.Value);
        Assert.Equal(4, match.Index);
    }

    /// <summary>The engine writes a named group the way .NET does not, and compiling one of its patterns has to allow for it.</summary>
    [Fact]
    public void Matches_APatternTheEngineShipsItsOwnExpressionFor_IsCompiledHereAnyway()
    {
        // Arrange
        var rule = SecretRuleCorpus.Rules.Single(definition => definition.Rule.Name == "UrlCredentials");

        // Act
        var matches = this.engine.Matches(
            "clone https://ada:hunter2@git.example.invalid/platform.git",
            rule.Pattern.Pattern,
            rule.Pattern.RegexOptions,
            captureGroup: "refine");

        // Assert
        Assert.Equal("ada:hunter2", Assert.Single(matches).Value);
    }

    /// <summary>Text this scanner reads is untrusted, so no expression may run without a ceiling on how long it may take.</summary>
    /// <remarks>
    /// The engine's own cache builds every expression with no match timeout at all, which is exactly what this type
    /// exists to replace for the patterns it ships. Both paths are asserted, because only one of them is compiled here
    /// and it would be the other that inherited the absence.
    /// </remarks>
    [Fact]
    public void MatcherFor_EitherPath_CarriesTheMatchTimeout()
    {
        // Arrange
        var expected = TimeSpan.FromMilliseconds(SecretRegexEngine.MatchTimeoutMilliseconds);
        var compiledHere = SecretRuleCorpus.Rules.First(definition => definition.Expression is not null).Pattern;
        var shippedByTheEngine = SecretRuleCorpus.Rules.First(definition => definition.Expression is null).Pattern;

        // Act
        var compiled = this.engine.MatcherFor(compiledHere.Pattern, compiledHere.RegexOptions);
        var adopted = this.engine.MatcherFor(shippedByTheEngine.Pattern, shippedByTheEngine.RegexOptions);

        // Assert
        Assert.Equal(expected, compiled.MatchTimeout);
        Assert.Equal(expected, adopted.MatchTimeout);
    }

    /// <summary>Compiling an expression per call would put the whole corpus's construction on every scan.</summary>
    [Fact]
    public void MatcherFor_TheSamePatternTwice_ReturnsTheSameMatcher()
    {
        // Arrange
        var shippedByTheEngine = SecretRuleCorpus.Rules.First(definition => definition.Expression is null).Pattern;

        // Act
        var first = this.engine.MatcherFor(shippedByTheEngine.Pattern, shippedByTheEngine.RegexOptions);
        var second = this.engine.MatcherFor(shippedByTheEngine.Pattern, shippedByTheEngine.RegexOptions);

        // Assert
        Assert.Same(first, second);
    }

    /// <summary>Without a capture group the region is the whole match, which is what an expression with no refinement wants.</summary>
    [Fact]
    public void Matches_NoCaptureGroupAsked_ReportsTheWholeMatch()
    {
        // Act
        var matches = this.engine.Matches("value 4711 end", @"\b[0-9]{4}\b", RegexOptions.CultureInvariant);

        // Assert
        Assert.Equal("4711", Assert.Single(matches).Value);
    }
}
