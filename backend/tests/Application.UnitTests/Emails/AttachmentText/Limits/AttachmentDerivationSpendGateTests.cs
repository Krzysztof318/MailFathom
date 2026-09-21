// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText.Limits;
using MailFathom.Application.Persistence;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.AttachmentText.Limits;

/// <summary>Covers what a mailbox's reading is admitted against and what a charge against one moves.</summary>
/// <remarks>
/// A pass holds the mailbox it is walking and never a person, so everything here is about the fan-out between the two:
/// which of a shared mailbox's users decides, what a mailbox nobody is assigned is admitted under, and what one
/// reading moves once against what it moves per user.
/// </remarks>
public sealed class AttachmentDerivationSpendGateTests
{
    private static readonly DateTimeOffset Midday = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset PeriodStart = new(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// ADR 0014 counts a shared mailbox in full against every user assigned it, so reading it proceeds only while
    /// every one of them is under their ceiling.
    /// </summary>
    [Fact]
    public async Task ReadCurrentPeriodForAccountAsync_OneAssignedUserOfTwoHasReadTheirShare_RefusesTheMailbox()
    {
        // Arrange
        var ledger = new InMemoryAttachmentDerivationSpendLedger();
        ledger.Seed(PeriodStart, AttachmentDerivationStep.Extraction, SyntheticUser.Another, 4_096);
        var gate = CreateGate(
            ledger,
            Bounded(maxInputOctetsPerPeriod: 1_000_000, maxInputOctetsPerPeriodPerUser: 4_096),
            new StubMailAccountAssignments()
                .Assigning(SyntheticUser.Deployment, SyntheticMailAccount.Deployment)
                .Assigning(SyntheticUser.Another, SyntheticMailAccount.Deployment));

        // Act
        var admission = await gate.ReadCurrentPeriodForAccountAsync(
            AttachmentDerivationStep.Extraction,
            SyntheticMailAccount.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(admission.AdmitsWork);
        Assert.Equal(AttachmentDerivationBound.User, admission.ReachedBound);
    }

    /// <summary>
    /// A mailbox nobody is assigned has no per-user allowance to be admitted under, and every caller-facing scope
    /// narrows to the accounts somebody is assigned — so reading it would open attachments nobody could reach, under
    /// no per-user ceiling at all, for as long as it stayed unassigned.
    /// </summary>
    [Theory]
    [InlineData(AttachmentDerivationStep.Extraction)]
    [InlineData(AttachmentDerivationStep.Description)]
    public async Task ReadCurrentPeriodForAccountAsync_AMailboxNobodyIsAssigned_AdmitsNothingWhateverIsDeclared(
        AttachmentDerivationStep derivationStep)
    {
        // Arrange
        var gate = CreateGate(
            new InMemoryAttachmentDerivationSpendLedger(),
            AttachmentDerivationBudget.Unbounded,
            new StubMailAccountAssignments());

        // Act
        var admission = await gate.ReadCurrentPeriodForAccountAsync(
            derivationStep,
            SyntheticMailAccount.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(admission.AdmitsWork);
        Assert.Equal(AttachmentDerivationBound.User, admission.ReachedBound);
    }

    /// <summary>
    /// What one reading opened is one figure however many people are assigned the mailbox, so the deployment's own
    /// row moves by it once while each user is charged in full. A deployment total read off the users' sum would stop
    /// a mailbox three people share after a third of what the operator declared.
    /// </summary>
    [Fact]
    public async Task RecordAccountSpendAsync_AMailboxTwoUsersShare_ChargesEachInFullAndTheDeploymentOnce()
    {
        // Arrange
        var ledger = new InMemoryAttachmentDerivationSpendLedger();
        var gate = CreateGate(
            ledger,
            Bounded(maxInputOctetsPerPeriod: 1_000_000, maxInputOctetsPerPeriodPerUser: 1_000_000),
            new StubMailAccountAssignments()
                .Assigning(SyntheticUser.Deployment, SyntheticMailAccount.Deployment)
                .Assigning(SyntheticUser.Another, SyntheticMailAccount.Deployment));

        // Act
        await gate.RecordAccountSpendAsync(
            Substitute.For<IPersistenceSession>(),
            AttachmentDerivationStep.Extraction,
            SyntheticMailAccount.Deployment,
            unitCount: 4_096,
            TestContext.Current.CancellationToken);

        // Assert
        var deployment = await ledger.ReadDeploymentConsumedAsync(
            PeriodStart,
            AttachmentDerivationStep.Extraction,
            TestContext.Current.CancellationToken);

        Assert.Equal(4_096, deployment);
        Assert.Equal(
            4_096,
            ledger.Consumed[(PeriodStart, AttachmentDerivationStep.Extraction, SyntheticUser.Deployment)]);
        Assert.Equal(
            4_096,
            ledger.Consumed[(PeriodStart, AttachmentDerivationStep.Extraction, SyntheticUser.Another)]);
    }

    /// <summary>A mailbox nobody is assigned still moves the deployment's figure, which is what an operator watches.</summary>
    [Fact]
    public async Task RecordAccountSpendAsync_AMailboxNobodyIsAssigned_StillChargesTheDeployment()
    {
        // Arrange
        var ledger = new InMemoryAttachmentDerivationSpendLedger();
        var gate = CreateGate(ledger, AttachmentDerivationBudget.Unbounded, new StubMailAccountAssignments());

        // Act
        await gate.RecordAccountSpendAsync(
            Substitute.For<IPersistenceSession>(),
            AttachmentDerivationStep.Description,
            SyntheticMailAccount.Deployment,
            unitCount: 1,
            TestContext.Current.CancellationToken);

        // Assert
        var deployment = await ledger.ReadDeploymentConsumedAsync(
            PeriodStart,
            AttachmentDerivationStep.Description,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, deployment);
    }

    [Fact]
    public void Constructor_AMissingCollaborator_IsRefused()
    {
        // Arrange
        var ledger = new InMemoryAttachmentDerivationSpendLedger();
        var assignments = new StubMailAccountAssignments();
        var budget = AttachmentDerivationBudget.Unbounded;
        var timeProvider = new FakeTimeProvider(Midday);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new AttachmentDerivationSpendGate(null!, assignments, budget, timeProvider));
        Assert.Throws<ArgumentNullException>(() => new AttachmentDerivationSpendGate(ledger, null!, budget, timeProvider));
        Assert.Throws<ArgumentNullException>(() => new AttachmentDerivationSpendGate(ledger, assignments, null!, timeProvider));
        Assert.Throws<ArgumentNullException>(() => new AttachmentDerivationSpendGate(ledger, assignments, budget, null!));
    }

    private static AttachmentDerivationBudget Bounded(
        long maxInputOctetsPerPeriod,
        long maxInputOctetsPerPeriodPerUser) =>
        AttachmentDerivationBudget.Create(
            maxInputOctetsPerPeriod,
            maxInputOctetsPerPeriodPerUser,
            maxDescriptionsPerPeriod: 0,
            maxDescriptionsPerPeriodPerUser: 0,
            TimeSpan.FromDays(1));

    private static AttachmentDerivationSpendGate CreateGate(
        IAttachmentDerivationSpendLedger ledger,
        AttachmentDerivationBudget budget,
        StubMailAccountAssignments assignments) =>
        new(ledger, assignments, budget, new FakeTimeProvider(Midday));
}
