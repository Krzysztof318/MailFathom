// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Delivery;
using MailFathom.SyntheticMail.Generation;
using MailFathom.SyntheticMail.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using MimeKit;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Delivery;

/// <summary>How a batch paces itself, what it does with a refusal, and what it says afterwards.</summary>
public sealed class SyntheticMailBatchDeliveryTests
{
    private static readonly MailboxAddress Recipient = new("Developer", "developer@example.com");

    [Fact]
    public async Task DeliverAsync_EveryMessageAccepted_ReportsThemAllDelivered()
    {
        // Arrange
        await using var transport = new RecordingSyntheticMailTransport();
        var corpus = Corpus(5);

        // Act
        var report = await Deliver(transport, corpus);

        // Assert
        Assert.Equal(5, report.Attempted);
        Assert.Equal(5, report.Delivered);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public async Task DeliverAsync_AMessageTheServerRefuses_KeepsGoingAndNamesIt()
    {
        // Arrange
        var corpus = Corpus(5);
        var refused = corpus[2];
        await using var transport = new RecordingSyntheticMailTransport(
            message => message.MessageId == refused.MessageId ? "552 message too large" : null);

        // Act
        var report = await Deliver(transport, corpus);

        // Assert
        Assert.Equal(5, transport.Submissions.Count);
        Assert.Equal(4, report.Delivered);

        var failure = Assert.Single(report.Failures);

        Assert.Equal(refused.MessageId, failure.MessageId);
        Assert.Equal(refused.Subject, failure.Subject);
        Assert.Equal("552 message too large", failure.Reason);
    }

    [Fact]
    public async Task DeliverAsync_Always_SubmitsToTheNamedRecipientAloneWhateverTheHeadersSay()
    {
        // Arrange
        await using var transport = new RecordingSyntheticMailTransport();

        // Act
        await Deliver(transport, Corpus(20));

        // Assert
        // The invented participants stay in the headers and reach the envelope of nothing, so a reserved-domain
        // address the server would never resolve is never a delivery it attempts.
        Assert.All(transport.Submissions, submission => Assert.Equal([Recipient.Address], submission.EnvelopeRecipients));
        Assert.Contains(transport.Submissions, submission => submission.Cc.Count > 0);
        Assert.All(
            transport.Submissions.SelectMany(submission => submission.Cc.Concat(submission.From)),
            address => Assert.EndsWith(SyntheticVocabulary.ReservedTopLevelDomain, address, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeliverAsync_ABatch_SaysWhatItIsSubmittingWhileItSubmits()
    {
        // Arrange
        await using var transport = new RecordingSyntheticMailTransport();
        var console = new RecordingSyntheticMailConsole();
        var delivery = new SyntheticMailBatchDelivery(transport, console, new FakeTimeProvider());

        // Act
        await delivery.DeliverAsync(
            Corpus(3),
            Account(),
            Recipient,
            TimeSpan.Zero,
            TestContext.Current.CancellationToken);

        // Assert
        // A run that submits two hundred messages over a paced connection says nothing for minutes otherwise, and a
        // developer cannot tell that from a run that has hung. The corpus is not what carries it: progress belongs to
        // the stream a run reports itself on.
        Assert.Equal(
            ["Submitting 1 of 3 to developer@example.com.", "Submitting 2 of 3 to developer@example.com.", "Submitting 3 of 3 to developer@example.com."],
            console.Diagnostics);
        Assert.Empty(console.Output);
    }

    [Fact]
    public async Task DeliverAsync_AnInterval_WaitsItOutBetweenTwoSubmissions()
    {
        // Arrange
        await using var transport = new RecordingSyntheticMailTransport();
        var timeProvider = new FakeTimeProvider();
        var delivery = new SyntheticMailBatchDelivery(transport, new RecordingSyntheticMailConsole(), timeProvider);

        // Act
        var run = delivery.DeliverAsync(
            Corpus(2),
            Account(),
            Recipient,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        // Assert
        // The first message goes out at once and the second waits on the clock, so the batch is unfinished and has
        // submitted exactly one thing until time moves.
        Assert.Single(transport.Submissions);
        Assert.False(run.IsCompleted);

        timeProvider.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(2, (await run).Delivered);
    }

    [Fact]
    public async Task DeliverAsync_NoInterval_SubmitsWithoutWaiting()
    {
        // Arrange
        await using var transport = new RecordingSyntheticMailTransport();
        var timeProvider = new FakeTimeProvider();
        var delivery = new SyntheticMailBatchDelivery(transport, new RecordingSyntheticMailConsole(), timeProvider);

        // Act
        var report = await delivery.DeliverAsync(
            Corpus(4),
            Account(),
            Recipient,
            TimeSpan.Zero,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4, report.Delivered);
    }

    [Fact]
    public async Task DeliverAsync_AnEmptyCorpus_SubmitsNothing()
    {
        // Arrange
        await using var transport = new RecordingSyntheticMailTransport();

        // Act
        var report = await Deliver(transport, []);

        // Assert
        Assert.Empty(transport.Submissions);
        Assert.Equal(0, report.Attempted);
    }

    [Fact]
    public async Task DeliverAsync_ANullArgument_IsRefused()
    {
        // Arrange
        await using var transport = new RecordingSyntheticMailTransport();
        var delivery = new SyntheticMailBatchDelivery(transport, new RecordingSyntheticMailConsole(), TimeProvider.System);

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => delivery.DeliverAsync(
            null!,
            Account(),
            Recipient,
            TimeSpan.Zero,
            TestContext.Current.CancellationToken));
    }

    private static Task<DeliveryReport> Deliver(
        ISyntheticMailTransport transport,
        IReadOnlyList<SyntheticEmail> corpus) =>
        new SyntheticMailBatchDelivery(transport, new RecordingSyntheticMailConsole(), new FakeTimeProvider()).DeliverAsync(
            corpus,
            Account(),
            Recipient,
            TimeSpan.Zero,
            TestContext.Current.CancellationToken);

    private static IReadOnlyList<SyntheticEmail> Corpus(int count) => SyntheticEmailGenerator.Generate(
        new SyntheticCorpusPlan(
            Seed: 99,
            count,
            new DateTimeOffset(2026, 8, 8, 23, 59, 59, TimeSpan.Zero),
            SpanDays: 30,
            MaximumAttachmentBytes: 256,
            SensitivePercentage: 25,
            Languages: [],
            Topics: []));

    private static SendingAccount Account() => new(
        "smtp.example.test",
        587,
        MailTransportSecurity.StartTls,
        new MailboxAddress("Throwaway", "throwaway@example.test"),
        "throwaway@example.test",
        "not-a-real-password",
        SyntheticAuthorIdentity.Fabricated);
}
