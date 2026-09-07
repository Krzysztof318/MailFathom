// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText.Limits;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.AttachmentText;

/// <summary>Covers what a deployment's attachment ceilings admit, and which of the four each question resolves to.</summary>
/// <remarks>
/// The four ceilings are two steps counted in two scopes, and the mistake this suite exists to catch is a step reading
/// the other one's figure: octets read and description calls are different quantities, so a budget that answered the
/// extraction question with the description ceiling would refuse and admit work at numbers nothing declared.
/// </remarks>
public sealed class AttachmentDerivationBudgetTests
{
    /// <summary>Each step and scope resolves to the ceiling declared for it, and to no other.</summary>
    [Fact]
    public void CeilingFor_EachStepAndScope_ResolvesToItsOwnDeclaredCeiling()
    {
        // Arrange
        var budget = AttachmentDerivationBudget.Create(
            maxInputOctetsPerPeriod: 4096,
            maxInputOctetsPerPeriodPerUser: 1024,
            maxDescriptionsPerPeriod: 40,
            maxDescriptionsPerPeriodPerUser: 10,
            TimeSpan.FromDays(1));

        // Act, Assert
        Assert.Equal(4096, budget.CeilingFor(AttachmentDerivationStep.Extraction, forUser: false));
        Assert.Equal(1024, budget.CeilingFor(AttachmentDerivationStep.Extraction, forUser: true));
        Assert.Equal(40, budget.CeilingFor(AttachmentDerivationStep.Description, forUser: false));
        Assert.Equal(10, budget.CeilingFor(AttachmentDerivationStep.Description, forUser: true));
    }

    /// <summary>A step outside the set is refused rather than answered with a ceiling nobody declared.</summary>
    [Fact]
    public void CeilingFor_AStepOutsideTheSet_IsRefused()
    {
        // Arrange
        var budget = AttachmentDerivationBudget.Unbounded;

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.CeilingFor((AttachmentDerivationStep)7, forUser: false));
    }

    /// <summary>The default a deployment starts on declares nothing, in either step and either scope.</summary>
    [Fact]
    public void Unbounded_EveryStepAndScope_DeclaresNoCeiling()
    {
        // Arrange
        var budget = AttachmentDerivationBudget.Unbounded;

        // Act, Assert
        Assert.True(budget.IsUnbounded);
        Assert.Equal(0, budget.CeilingFor(AttachmentDerivationStep.Extraction, forUser: false));
        Assert.Equal(0, budget.CeilingFor(AttachmentDerivationStep.Description, forUser: false));
    }

    /// <summary>A negative ceiling is refused, because a period that admits less than nothing is not a budget.</summary>
    [Fact]
    public void Create_ANegativeCeiling_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => AttachmentDerivationBudget.Create(
            maxInputOctetsPerPeriod: -1,
            maxInputOctetsPerPeriodPerUser: 0,
            maxDescriptionsPerPeriod: 0,
            maxDescriptionsPerPeriodPerUser: 0,
            TimeSpan.FromDays(1)));
    }

    /// <summary>A period is anchored to the epoch rather than to when the process started, so two hosts agree on it.</summary>
    [Fact]
    public void PeriodStartAt_AnInstantInsideADay_IsThatDaysMidnight()
    {
        // Arrange
        var budget = AttachmentDerivationBudget.Unbounded;

        // Act
        var start = budget.PeriodStartAt(new DateTimeOffset(2026, 9, 6, 17, 42, 3, TimeSpan.Zero));

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero), start);
    }

    /// <summary>A period that has consumed exactly its ceiling admits nothing more, which is what stops a pass.</summary>
    [Fact]
    public void AdmitsWork_APeriodAtItsCeiling_IsExhausted()
    {
        // Arrange
        var start = new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
        AttachmentDerivationPeriod period = new(
            AttachmentDerivationStep.Extraction,
            start,
            start.AddDays(1),
            ConsumedUnitCount: 4096,
            CeilingUnitCount: 4096);

        // Act, Assert
        Assert.True(period.IsExhausted);
        Assert.False(period.AdmitsWork);
        Assert.Equal(0, period.RemainingUnitCount);
    }

    /// <summary>A period against no declared ceiling admits work however much it has already counted.</summary>
    [Fact]
    public void AdmitsWork_APeriodWithNoCeiling_AdmitsWorkWhateverItHasCounted()
    {
        // Arrange
        var start = new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
        AttachmentDerivationPeriod period = new(
            AttachmentDerivationStep.Description,
            start,
            start.AddDays(1),
            ConsumedUnitCount: 9_000_000,
            CeilingUnitCount: null);

        // Act, Assert
        Assert.False(period.IsExhausted);
        Assert.True(period.AdmitsWork);
        Assert.Null(period.RemainingUnitCount);
    }
}
