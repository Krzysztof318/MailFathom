// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation;
using Microsoft.UI.Xaml;

namespace MailFathom.Client.UnitTests.Presentation;

/// <summary>What turns something the session stated into a control being on the screen or absent from it.</summary>
public sealed class AffirmedVisibilityConverterTests
{
    private readonly AffirmedVisibilityConverter converter = new();

    /// <summary>An outright yes is the only thing that puts a capability in front of somebody.</summary>
    [Fact]
    public void Convert_ACapabilityTheSessionOffers_PutsTheControlOnTheScreen()
    {
        // Act
        var visibility = this.converter.Convert(true, typeof(Visibility), null, null);

        // Assert
        Assert.Equal(Visibility.Visible, visibility);
    }

    /// <summary>
    /// Everything else collapses, and the unknown case is the reason that matters: a property carrying no answer yet
    /// — the session is still being fetched, or the fetch failed — reaches a binding as nothing at all or as the
    /// type's default, and showing a control on that would put a space in front of somebody before anything said they
    /// may use it, or announce one as withheld while the fetch that decides it was still running. Both sides are
    /// stated as their own affirmative for exactly this reason, which is why one rule serves them both.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    [InlineData("true")]
    public void Convert_AnythingThatIsNotAnOutrightYes_LeavesTheControlAbsent(object? value)
    {
        // Act
        var visibility = this.converter.Convert(value, typeof(Visibility), null, null);

        // Assert
        Assert.Equal(Visibility.Collapsed, visibility);
    }

    /// <summary>What the client offers is read from the session, so a control's visibility is never written back to it.</summary>
    [Fact]
    public void ConvertBack_AnyVisibility_IsRefused()
    {
        // Act, Assert
        Assert.Throws<NotSupportedException>(
            () => this.converter.ConvertBack(Visibility.Visible, typeof(bool), null, null));
    }
}
