// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Mail.Dkim;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Dkim;

public sealed class DkimPublicKeyRecordCacheTests
{
    private const string Name = "MAILFATHOM._DOMAINKEY.SIGNER.EXAMPLE.TEST";
    private const string Record = "v=DKIM1; k=rsa; p=key";

    /// <summary>A held record is answered without the resolver being asked again.</summary>
    [Fact]
    public void TryRead_ARecordStillWithinItsLifetime_IsAnswered()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var cache = new DkimPublicKeyRecordCache(clock);
        cache.Store(Name, Record, TimeSpan.FromHours(1));

        // Act
        var held = cache.TryRead(Name, out var record);

        // Assert
        Assert.True(held);
        Assert.Equal(Record, record);
    }

    /// <summary>A record whose lifetime has run out is not answered, so the name is asked for again.</summary>
    [Fact]
    public void TryRead_ARecordPastItsLifetime_IsNotAnswered()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var cache = new DkimPublicKeyRecordCache(clock);
        cache.Store(Name, Record, TimeSpan.FromMinutes(5));

        // Act
        clock.Advance(TimeSpan.FromMinutes(5));

        // Assert
        Assert.False(cache.TryRead(Name, out _));
    }

    /// <summary>A very low time-to-live is not an invitation to one lookup per message, so it is floored.</summary>
    [Fact]
    public void Store_ATimeToLiveBelowTheFloor_IsHeldForTheFloor()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var cache = new DkimPublicKeyRecordCache(clock);

        // Act
        cache.Store(Name, Record, TimeSpan.FromSeconds(1));
        clock.Advance(TimeSpan.FromSeconds(59));

        // Assert
        Assert.True(cache.TryRead(Name, out _));
    }

    /// <summary>A time-to-live longer than a day is capped, so a rotated key is never held indefinitely.</summary>
    [Fact]
    public void Store_ATimeToLiveAboveTheCeiling_IsHeldForTheCeiling()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var cache = new DkimPublicKeyRecordCache(clock);

        // Act
        cache.Store(Name, Record, TimeSpan.FromDays(30));
        clock.Advance(TimeSpan.FromDays(1));

        // Assert
        Assert.False(cache.TryRead(Name, out _));
    }

    /// <summary>The absence of a record is held too, so a retired selector is not asked for on every message.</summary>
    [Fact]
    public void StoreAbsence_ANamePublishingNothing_IsAnsweredAsNoRecord()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var cache = new DkimPublicKeyRecordCache(clock);

        // Act
        cache.StoreAbsence(Name);
        var held = cache.TryRead(Name, out var record);

        // Assert
        Assert.True(held);
        Assert.Null(record);
    }

    /// <summary>An absence is held for less time than a record, so a brief outage is not remembered for a day.</summary>
    [Fact]
    public void StoreAbsence_AfterItsShorterLifetime_IsNoLongerAnswered()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var cache = new DkimPublicKeyRecordCache(clock);
        cache.StoreAbsence(Name);

        // Act
        clock.Advance(TimeSpan.FromMinutes(5));

        // Assert
        Assert.False(cache.TryRead(Name, out _));
    }

    /// <summary>The cache is bounded in entries, because its keys come from mail rather than from configuration.</summary>
    /// <remarks>
    /// What the bound gives up is stated by the assertion: reaching it starts the cache over, so an entry stored before
    /// it is no longer answered and costs one lookup. What it buys is that a sender writing from unlimited subdomains
    /// cannot grow the cache without limit.
    /// </remarks>
    [Fact]
    public void Store_MoreNamesThanTheBoundHolds_StartsOver()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var cache = new DkimPublicKeyRecordCache(clock);
        cache.Store(Name, Record, TimeSpan.FromHours(1));

        // Act
        foreach (var index in Enumerable.Range(0, 2048))
        {
            cache.Store($"MAILFATHOM._DOMAINKEY.SIGNER{index}.EXAMPLE.TEST", Record, TimeSpan.FromHours(1));
        }

        // Assert
        Assert.False(cache.TryRead(Name, out _));
    }

    /// <summary>Re-storing a name the cache already holds replaces it rather than counting as a new entry.</summary>
    [Fact]
    public void Store_ANameAlreadyHeld_ReplacesWhatItHolds()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var cache = new DkimPublicKeyRecordCache(clock);
        cache.StoreAbsence(Name);

        // Act
        cache.Store(Name, Record, TimeSpan.FromHours(1));

        // Assert
        Assert.True(cache.TryRead(Name, out var record));
        Assert.Equal(Record, record);
    }

    /// <summary>A name nothing was ever stored for is not answered.</summary>
    [Fact]
    public void TryRead_ANameNothingWasStoredFor_IsNotAnswered()
    {
        // Arrange
        var cache = new DkimPublicKeyRecordCache(new FakeTimeProvider());

        // Act, Assert
        Assert.False(cache.TryRead(Name, out _));
    }
}
