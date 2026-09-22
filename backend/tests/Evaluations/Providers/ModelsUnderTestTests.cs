// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Xunit;

namespace MailFathom.Evaluations.Providers;

public sealed class ModelsUnderTestTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("low")]
    [InlineData("high")]
    public void ParseReasoningEffort_NoneOrOneWordALevelIsWrittenAs_ReadsItAsDeclared(string? declared)
    {
        // Act
        var effort = ModelsUnderTest.ParseReasoningEffort(declared);

        // Assert
        Assert.Equal(declared, effort);
    }

    [Theory]
    [InlineData("very high")]
    [InlineData("high;low")]
    public void ParseReasoningEffort_AnythingElse_FailsNamingTheVariableAndNotTheValue(string declared)
    {
        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => ModelsUnderTest.ParseReasoningEffort(declared));

        // Assert
        Assert.Contains(ModelsUnderTest.ReasoningEffortVariable, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(declared, failure.Message, StringComparison.Ordinal);
    }
}
