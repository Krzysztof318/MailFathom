// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Presentation.Mailboxes;

namespace MailFathom.Client.UnitTests.Presentation.Mailboxes;

/// <summary>How the tree's arrangement survives being written as one value and read back out of it.</summary>
/// <remarks>
/// What is behind the store is <c>ApplicationData.LocalSettings</c>, which no unit test can reach, so what is asserted
/// here is the pair either side of it: the composition that goes in and the division that comes back. That pair is
/// where a mail server's own folder names arrive, which is what makes it worth asserting apart from the store.
/// </remarks>
public sealed class LocalSettingsMailboxTreeMemoryTests
{
    /// <summary>What was written is what comes back, which is the whole of what one entry per row has to do.</summary>
    [Fact]
    public void Divided_TheKeysThatWereJoined_ReadsThemBack()
    {
        // Arrange
        var expanded = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            MailboxTreeShape.AccountKey("work"),
            MailboxTreeShape.LevelKey("work", ["Projects", "2024"]));

        // Act
        var read = LocalSettingsMailboxTreeMemory.Divided(LocalSettingsMailboxTreeMemory.Joined(expanded));

        // Assert
        Assert.Equal(expanded.Order(StringComparer.Ordinal), read.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A folder name is a mail server's to choose, so one carrying the separator the entry is joined by is dropped
    /// rather than written. Writing it would make the split disagree with the join, which is not that row losing its
    /// expansion but every row beside it being merged or divided at the wrong place.
    /// </summary>
    [Fact]
    public void Joined_AFolderNameCarryingTheSeparator_IsLeftOutRatherThanCorruptingTheRest()
    {
        // Arrange
        var kept = MailboxTreeShape.AccountKey("work");
        var expanded = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            kept,
            MailboxTreeShape.LevelKey("work", ["Invoices\nand receipts"]));

        // Act
        var read = LocalSettingsMailboxTreeMemory.Divided(LocalSettingsMailboxTreeMemory.Joined(expanded));

        // Assert
        Assert.Equal([kept], read);
    }

    /// <summary>A store holding nothing is a first run rather than a failure, and so is one holding an empty entry.</summary>
    [Fact]
    public void Divided_AStoreHoldingNothing_ReadsAsNothingHavingBeenRemembered()
    {
        // Act, Assert
        Assert.Empty(LocalSettingsMailboxTreeMemory.Divided(null));
        Assert.Empty(LocalSettingsMailboxTreeMemory.Divided(string.Empty));
        Assert.Empty(LocalSettingsMailboxTreeMemory.Divided("\n\n"));
    }

    /// <summary>Nothing expanded is written as nothing, which is what lets the entry be removed rather than kept empty.</summary>
    [Fact]
    public void Joined_NothingExpanded_ComposesNothingToKeep()
    {
        // Act, Assert
        Assert.Empty(LocalSettingsMailboxTreeMemory.Joined(ImmutableHashSet<string>.Empty));
    }

    /// <summary>A composition without the keys to compose would be one writing an entry describing nothing.</summary>
    [Fact]
    public void Joined_MissingKeys_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => LocalSettingsMailboxTreeMemory.Joined(null!));
    }
}
