// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts.Collection;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests.Contacts.Collection;

/// <summary>Covers what one account is willing to have written into the collected half of its book.</summary>
public sealed class ContactCollectionPolicyTests
{
    /// <summary>An ordinary correspondent is what the whole feature exists to record.</summary>
    [Fact]
    public void Admits_AnOrdinaryAddress_IsAdmitted()
    {
        // Arrange
        var policy = ContactCollectionPolicy.Create([], []);

        // Act & Assert
        Assert.True(policy.Admits(AddressOf("anna@example.test")));
    }

    /// <summary>The structural rule holds without an owner writing anything, which is the point of it being structural.</summary>
    [Fact]
    public void Admits_AnAutomatedMailbox_IsRefusedWithNoExclusionWritten()
    {
        // Arrange
        var policy = ContactCollectionPolicy.Create([], []);

        // Act & Assert
        Assert.False(policy.Admits(AddressOf("no-reply@example.test")));
    }

    /// <summary>A book holding its owner would answer "who is this from" with the person asking.</summary>
    [Fact]
    public void Admits_TheDeploymentsOwnAddress_IsRefused()
    {
        // Arrange
        var policy = ContactCollectionPolicy.Create([], [AddressOf("owner@example.test")]);

        // Act & Assert
        Assert.False(policy.Admits(AddressOf("Owner@Example.test")));
        Assert.True(policy.Admits(AddressOf("anna@example.test")));
    }

    /// <summary>The owner's list is held against every address, whichever of the two shapes each entry took.</summary>
    [Fact]
    public void Admits_AnExcludedAddress_IsRefused()
    {
        // Arrange
        Assert.True(ContactCollectionExclusion.TryCreateForDomain("lists.test", includeSubdomains: true, out var domain));
        Assert.True(ContactCollectionExclusion.TryCreateForAddressPattern("bot-*@*", out var pattern));

        var policy = ContactCollectionPolicy.Create([domain, pattern], []);

        // Act & Assert
        Assert.False(policy.Admits(AddressOf("announce@mail.lists.test")));
        Assert.False(policy.Admits(AddressOf("bot-nightly@example.test")));
        Assert.True(policy.Admits(AddressOf("anna@example.test")));
    }

    /// <summary>A message a distributor or a program stamped is not correspondence, whatever addresses it carries.</summary>
    [Theory]
    [InlineData(EmailAutomation.None, true)]
    [InlineData(EmailAutomation.MailingList, false)]
    [InlineData(EmailAutomation.AutomaticallySubmitted, false)]
    [InlineData(EmailAutomation.BulkPrecedence, false)]
    public void Admits_WhatTheMessageClaimedAboutItself_DecidesTheWholeMessage(EmailAutomation automation, bool expected)
    {
        // Arrange
        var policy = ContactCollectionPolicy.Create([], []);

        // Act & Assert
        Assert.Equal(expected, policy.Admits(automation));
    }

    /// <summary>The policy an account that never reaches collection carries narrows nothing, and nothing asks it.</summary>
    [Fact]
    public void NothingExcluded_HoldsNoEntriesAndStillAppliesTheStructuralRule()
    {
        // Act & Assert
        Assert.Empty(ContactCollectionPolicy.NothingExcluded.Exclusions);
        Assert.True(ContactCollectionPolicy.NothingExcluded.Admits(AddressOf("anna@example.test")));
        Assert.False(ContactCollectionPolicy.NothingExcluded.Admits(AddressOf("postmaster@example.test")));
    }

    private static EmailAddress AddressOf(string value)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, value, out var address));

        return address;
    }
}
