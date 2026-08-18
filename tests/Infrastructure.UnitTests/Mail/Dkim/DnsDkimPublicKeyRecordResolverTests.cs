// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using DnsClient;
using DnsClient.Protocol;
using MailFathom.Infrastructure.Mail.Dkim;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Dkim;

public sealed class DnsDkimPublicKeyRecordResolverTests
{
    private const string Selector = "mailfathom";
    private const string SigningDomain = "signer.example.test";
    private const string KeyRecordName = "mailfathom._domainkey.signer.example.test";

    /// <summary>The name asked for is the one RFC 6376 publishes a key at, assembled from the signature's own tags.</summary>
    [Fact]
    public async Task ResolveAsync_ASelectorAndDomain_AsksForTheKeyRecordName()
    {
        // Arrange
        var lookup = LookupAnswering(TxtRecordOf("v=DKIM1; p=key"));

        // Act
        await CreateResolver(lookup).ResolveAsync(Selector, SigningDomain, CancellationToken.None);

        // Assert
        await lookup.Received(1).QueryAsync(
            KeyRecordName,
            QueryType.TXT,
            QueryClass.IN,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A record published as several strings is one value, so the strings are joined with nothing between them.</summary>
    [Fact]
    public async Task ResolveAsync_ARecordPublishedAsSeveralStrings_JoinsThem()
    {
        // Arrange
        var lookup = LookupAnswering(TxtRecordOf("v=DKIM1; k=rsa; p=AAAA", "BBBB"));

        // Act
        var record = await CreateResolver(lookup).ResolveAsync(Selector, SigningDomain, CancellationToken.None);

        // Assert
        Assert.Equal("v=DKIM1; k=rsa; p=AAAABBBB", record);
    }

    /// <summary>A name asked for once is not asked for again, which is what keeps a mailbox's lookups proportionate.</summary>
    [Fact]
    public async Task ResolveAsync_TheSameNameTwice_QueriesOnce()
    {
        // Arrange
        var lookup = LookupAnswering(TxtRecordOf("v=DKIM1; p=key"));
        var resolver = CreateResolver(lookup);

        // Act
        await resolver.ResolveAsync(Selector, SigningDomain, CancellationToken.None);
        var second = await resolver.ResolveAsync(Selector, SigningDomain, CancellationToken.None);

        // Assert
        Assert.Equal("v=DKIM1; p=key", second);
        await lookup.Received(1).QueryAsync(
            Arg.Any<string>(),
            Arg.Any<QueryType>(),
            Arg.Any<QueryClass>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A name publishing nothing answers with no record, and is not asked for again either.</summary>
    [Fact]
    public async Task ResolveAsync_ANamePublishingNothing_AnswersWithNoRecordAndHoldsThat()
    {
        // Arrange
        var lookup = LookupAnswering();
        var resolver = CreateResolver(lookup);

        // Act
        var first = await resolver.ResolveAsync(Selector, SigningDomain, CancellationToken.None);
        var second = await resolver.ResolveAsync(Selector, SigningDomain, CancellationToken.None);

        // Assert
        Assert.Null(first);
        Assert.Null(second);
        await lookup.Received(1).QueryAsync(
            Arg.Any<string>(),
            Arg.Any<QueryType>(),
            Arg.Any<QueryClass>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A response the server answered with an error is no record rather than a failure travelling upwards.</summary>
    [Fact]
    public async Task ResolveAsync_AResponseCarryingAnError_AnswersWithNoRecord()
    {
        // Arrange
        var response = Substitute.For<IDnsQueryResponse>();
        response.HasError.Returns(true);
        response.Answers.Returns([]);

        var lookup = Substitute.For<IDnsQuery>();
        lookup
            .QueryAsync(Arg.Any<string>(), Arg.Any<QueryType>(), Arg.Any<QueryClass>(), Arg.Any<CancellationToken>())
            .Returns(response);

        // Act, Assert
        Assert.Null(await CreateResolver(lookup).ResolveAsync(Selector, SigningDomain, CancellationToken.None));
    }

    /// <summary>Every configured nameserver failing says nothing about the sender, so it answers with no record.</summary>
    [Fact]
    public async Task ResolveAsync_EveryNameserverFailing_AnswersWithNoRecord()
    {
        // Arrange
        var lookup = Substitute.For<IDnsQuery>();
        lookup
            .QueryAsync(Arg.Any<string>(), Arg.Any<QueryType>(), Arg.Any<QueryClass>(), Arg.Any<CancellationToken>())
            .Returns<IDnsQueryResponse>(_ => throw new DnsResponseException(DnsResponseCode.ConnectionTimeout));

        // Act, Assert
        Assert.Null(await CreateResolver(lookup).ResolveAsync(Selector, SigningDomain, CancellationToken.None));
    }

    /// <summary>A lookup outliving its deadline answers with no record rather than stalling the extraction behind it.</summary>
    [Fact]
    public async Task ResolveAsync_ALookupOutlivingItsDeadline_AnswersWithNoRecord()
    {
        // Arrange
        var lookup = Substitute.For<IDnsQuery>();
        lookup
            .QueryAsync(Arg.Any<string>(), Arg.Any<QueryType>(), Arg.Any<QueryClass>(), Arg.Any<CancellationToken>())
            .Returns(call => WaitForeverAsync(call.Arg<CancellationToken>()));
        var resolver = new DnsDkimPublicKeyRecordResolver(
            lookup,
            new DkimPublicKeyRecordCache(new FakeTimeProvider()),
            TimeSpan.FromMilliseconds(20));

        // Act, Assert
        Assert.Null(await resolver.ResolveAsync(Selector, SigningDomain, CancellationToken.None));
    }

    /// <summary>The caller giving up is not a lookup failing, so its cancellation still travels.</summary>
    [Fact]
    public async Task ResolveAsync_TheCallerCancelling_Propagates()
    {
        // Arrange
        var lookup = Substitute.For<IDnsQuery>();
        lookup
            .QueryAsync(Arg.Any<string>(), Arg.Any<QueryType>(), Arg.Any<QueryClass>(), Arg.Any<CancellationToken>())
            .Returns(call => WaitForeverAsync(call.Arg<CancellationToken>()));
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateResolver(lookup).ResolveAsync(Selector, SigningDomain, caller.Token));
    }

    /// <summary>A selector no resolver would accept is refused here rather than handed to one.</summary>
    [Theory]
    [InlineData("has a space")]
    [InlineData("")]
    [InlineData("sel@ector")]
    public async Task ResolveAsync_ASelectorNoResolverWouldAccept_QueriesNothing(string selector)
    {
        // Arrange
        var lookup = LookupAnswering(TxtRecordOf("v=DKIM1; p=key"));

        // Act
        var record = await CreateResolver(lookup).ResolveAsync(selector, SigningDomain, CancellationToken.None);

        // Assert
        Assert.Null(record);
        await lookup.DidNotReceive().QueryAsync(
            Arg.Any<string>(),
            Arg.Any<QueryType>(),
            Arg.Any<QueryClass>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A record past the bound is not handed to a key parser, because it arrives from a server nobody controls.</summary>
    [Fact]
    public async Task ResolveAsync_AnOverLongRecord_AnswersWithNoRecord()
    {
        // Arrange
        var lookup = LookupAnswering(TxtRecordOf(new string('A', 4097)));

        // Act, Assert
        Assert.Null(await CreateResolver(lookup).ResolveAsync(Selector, SigningDomain, CancellationToken.None));
    }

    private static DnsDkimPublicKeyRecordResolver CreateResolver(IDnsQuery lookup) =>
        new(lookup, new DkimPublicKeyRecordCache(new FakeTimeProvider()));

    /// <summary>Answers every query with the records a test named, or with none at all.</summary>
    private static IDnsQuery LookupAnswering(params TxtRecord[] answers)
    {
        var response = Substitute.For<IDnsQueryResponse>();
        response.HasError.Returns(false);
        response.Answers.Returns(answers);

        var lookup = Substitute.For<IDnsQuery>();
        lookup
            .QueryAsync(Arg.Any<string>(), Arg.Any<QueryType>(), Arg.Any<QueryClass>(), Arg.Any<CancellationToken>())
            .Returns(response);

        return lookup;
    }

    private static TxtRecord TxtRecordOf(params string[] values) => new(
        new ResourceRecordInfo(KeyRecordName, ResourceRecordType.TXT, QueryClass.IN, timeToLive: 3600, rawDataLength: 0),
        values,
        values);

    private static async Task<IDnsQueryResponse> WaitForeverAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw new InvalidOperationException("The wait never completes.");
    }
}
