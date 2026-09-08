// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Generation;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Generation;

/// <summary>The markup set a message's HTML alternative is drawn from, and what each member promises the prompt.</summary>
public sealed class SyntheticMarkupDialectTests
{
    [Fact]
    public void All_EveryMemberHasADistinctName()
    {
        // Arrange, Act
        var names = SyntheticMarkupDialect.All.Select(dialect => dialect.ToString()).ToArray();

        // Assert
        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    [Fact]
    public void All_EveryMemberCarriesADescriptionNamingConstructsRatherThanAClient()
    {
        // Arrange, Act
        var descriptions = SyntheticMarkupDialect.All.Select(dialect => dialect.PromptDescription).ToArray();

        // Assert
        // A description that named the client and left the markup to the model would be answered with the well-formed
        // document a model writes by default, which is the corpus this set exists to replace.
        Assert.All(descriptions, description => Assert.False(string.IsNullOrWhiteSpace(description)));
        Assert.All(descriptions, description => Assert.Contains("<", description, StringComparison.Ordinal));
    }

    [Fact]
    public void All_OneMemberAsksForMarkupThatIsNotWellFormed()
    {
        // Arrange, Act
        var malformed = SyntheticMarkupDialect.LegacyMalformed.PromptDescription;

        // Assert
        // The one member whose defects are the point: every reader here recovers from broken markup rather than
        // refusing it, and a corpus that never carried any could not show which of those recoveries is wrong.
        Assert.Contains(SyntheticMarkupDialect.LegacyMalformed, SyntheticMarkupDialect.All);
        Assert.Contains("unclosed", malformed, StringComparison.Ordinal);
    }

    [Fact]
    public void All_NoMemberAsksForAConstructTheAnswerWouldBeRefusedFor()
    {
        // Arrange
        string[] refused = ["<script", "<iframe", "<object", "<embed", "javascript:"];

        // Act
        var descriptions = SyntheticMarkupDialect.All.Select(dialect => dialect.PromptDescription);

        // Assert
        // A dialect asking for something the answer check refuses would fail every run it was drawn for, which reads
        // as a model that cannot write the answer rather than as a prompt that contradicted itself.
        Assert.All(
            descriptions,
            description => Assert.All(
                refused,
                construct => Assert.DoesNotContain(construct, description, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void TheDefault_IsNotADialectAndSaysSo()
    {
        // Arrange
        SyntheticMarkupDialect dialect = default;

        // Act, Assert
        Assert.DoesNotContain(dialect, SyntheticMarkupDialect.All);
        Assert.Equal("(unspecified)", dialect.ToString());
        Assert.Throws<InvalidOperationException>(() => _ = dialect.PromptDescription);
    }

    [Fact]
    public void ToString_ADeclaredDialect_IsTheNameTheListingPrints()
    {
        // Arrange, Act
        var line = string.Join(", ", SyntheticMarkupDialect.All.Select(dialect => dialect.ToString()));

        // Assert
        Assert.Equal("word-outlook, campaign-template, gmail-composer, apple-mail, legacy-malformed", line);
    }

    [Fact]
    public void Equality_ADialectIsItsOwnValue()
    {
        // Arrange, Act, Assert
        Assert.Equal(SyntheticMarkupDialect.AppleMail, SyntheticMarkupDialect.AppleMail);
        Assert.NotEqual(SyntheticMarkupDialect.AppleMail, SyntheticMarkupDialect.GmailComposer);
        Assert.NotEqual(SyntheticMarkupDialect.AppleMail, default);
    }
}
