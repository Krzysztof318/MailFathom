// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Emails;
using Xunit;

namespace MailMcp.Application.UnitTests;

/// <summary>Covers the one bound every mailbox query page obeys.</summary>
public sealed class MailboxQueryPageSizeTests
{
    [Fact]
    public void FromRequested_NoPageSizeNamed_TakesTheDefault()
    {
        // Act
        var pageSize = MailboxQueryPageSize.FromRequested(null);

        // Assert
        Assert.Equal(MailboxQueryPageSize.DefaultValue, pageSize.Value);
        Assert.Equal(MailboxQueryPageSize.Default, pageSize);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(MailboxQueryPageSize.DefaultValue)]
    [InlineData(MailboxQueryPageSize.MaximumValue)]
    public void FromRequested_PageSizeInsideTheRange_IsTakenAsWritten(int requested)
    {
        // Act
        var pageSize = MailboxQueryPageSize.FromRequested(requested);

        // Assert
        Assert.Equal(requested, pageSize.Value);
        Assert.True(pageSize.IsSpecified);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(MailboxQueryPageSize.MaximumValue + 1)]
    [InlineData(int.MaxValue)]
    public void Create_PageSizeOutsideTheRange_IsRejected(int requested)
    {
        // Act
        var failure = Assert.Throws<MailboxQueryPageSizeOutOfRangeException>(() =>
            MailboxQueryPageSize.Create(requested));

        // Assert
        Assert.Equal(requested, failure.RequestedPageSize);
        Assert.Contains(MailboxQueryPageSize.MaximumValue.ToString(), failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The struct default is reachable and names no page, which is what <c>IsSpecified</c> reports.</summary>
    [Fact]
    public void IsSpecified_StructDefault_NamesNoPageSize()
    {
        // Act
        var unspecified = default(MailboxQueryPageSize);

        // Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Equal("0", unspecified.ToString());
    }

    [Fact]
    public void ToString_SpecifiedPageSize_IsTheNumber()
    {
        // Act
        var pageSize = MailboxQueryPageSize.Create(42);

        // Assert
        Assert.Equal("42", pageSize.ToString());
    }

    /// <summary>The default is smaller than the maximum, so a caller who has not chosen gets a cheap page.</summary>
    [Fact]
    public void DefaultValue_IsBelowTheMaximum()
    {
        // Assert
        Assert.True(MailboxQueryPageSize.DefaultValue < MailboxQueryPageSize.MaximumValue);
    }
}
