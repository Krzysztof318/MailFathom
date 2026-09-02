// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Spam;
using Xunit;

namespace MailFathom.Domain.UnitTests.Spam;

public sealed class SpamAssessmentTests
{
    /// <summary>The comparison is inclusive, which is how a scanner's own "15.0 / 5.0" answer reads.</summary>
    [Theory]
    [InlineData(15.0, 5.0, true)]
    [InlineData(5.0, 5.0, true)]
    [InlineData(4.999, 5.0, false)]
    [InlineData(-2.6, 5.0, false)]
    public void ClearsThreshold_AScoreAgainstAThreshold_IsSpamFromTheThresholdUp(
        double score,
        double threshold,
        bool expected)
    {
        // Arrange, Act
        var assessment = SpamAssessment.Create(score, threshold);

        // Assert
        Assert.Equal(expected, assessment.ClearsThreshold);
    }

    [Theory]
    [InlineData(double.NaN, 5.0)]
    [InlineData(double.PositiveInfinity, 5.0)]
    [InlineData(5.0, double.NaN)]
    [InlineData(5.0, double.NegativeInfinity)]
    public void Create_AValueThatIsNotFinite_IsRefusedRatherThanCompared(double score, double threshold)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => SpamAssessment.Create(score, threshold));
    }
}
