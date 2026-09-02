// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the shared exception-hierarchy assertion against sample types declared beside it.</summary>
/// <remarks>
/// The samples live here rather than in a production assembly, because a helper that reported a false pass would report
/// it in every suite that uses it, and only a deliberately non-compliant sample can prove the assertion still fails.
/// </remarks>
public sealed class ExceptionHierarchyAssertionTests
{
    private static readonly Assembly SampleAssembly = typeof(SampleBaseException).Assembly;

    [Fact]
    public void AssertEveryDeclaredExceptionDerivesFrom_ConcreteExceptionOutsideTheHierarchy_FailsAndNamesOnlyThatType()
    {
        // Act
        var failure = Record.Exception(() => ExceptionHierarchyAssertion.AssertEveryDeclaredExceptionDerivesFrom(
            SampleAssembly,
            typeof(SampleBaseException)));

        // Assert
        Assert.NotNull(failure);
        Assert.Contains(nameof(SampleUnrelatedException), failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(SampleCompliantException), failure.Message, StringComparison.Ordinal);
    }

    /// <summary>An abstract exception is never thrown, so it is not a way out of the hierarchy and is not reported as one.</summary>
    [Fact]
    public void AssertEveryDeclaredExceptionDerivesFrom_AbstractExceptionOutsideTheHierarchy_IsNotReported()
    {
        // Act
        var failure = Record.Exception(() => ExceptionHierarchyAssertion.AssertEveryDeclaredExceptionDerivesFrom(
            SampleAssembly,
            typeof(SampleBaseException)));

        // Assert
        Assert.NotNull(failure);
        Assert.DoesNotContain(nameof(SampleAbstractException), failure.Message, StringComparison.Ordinal);
    }

    /// <summary>An internal exception reaches no boundary, so a published code would name something nothing publishes.</summary>
    [Fact]
    public void AssertEveryDeclaredExceptionDerivesFrom_InternalExceptionOutsideTheHierarchy_IsNotReported()
    {
        // Arrange
        var internalException = new SampleInternalException();

        // Act
        var failure = Record.Exception(() => ExceptionHierarchyAssertion.AssertEveryDeclaredExceptionDerivesFrom(
            SampleAssembly,
            typeof(SampleBaseException)));

        // Assert
        Assert.NotNull(failure);
        Assert.DoesNotContain(internalException.GetType().Name, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssertEveryDeclaredExceptionDerivesFrom_EveryDeclaredExceptionInsideTheHierarchy_Passes()
    {
        // Act
        var failure = Record.Exception(() => ExceptionHierarchyAssertion.AssertEveryDeclaredExceptionDerivesFrom(
            SampleAssembly,
            typeof(Exception)));

        // Assert
        Assert.Null(failure);
    }
}

/// <summary>A sample hierarchy root standing in for a production base exception.</summary>
public abstract class SampleBaseException : Exception
{
    /// <summary>Initializes a new sample failure.</summary>
    /// <param name="message">The sample message.</param>
    protected SampleBaseException(string message)
        : base(message)
    {
    }
}

/// <summary>A sample exception that takes part in <see cref="SampleBaseException" />.</summary>
public sealed class SampleCompliantException : SampleBaseException
{
    /// <summary>Initializes a new compliant sample failure.</summary>
    public SampleCompliantException()
        : base("inside")
    {
    }
}

/// <summary>A sample exception that deliberately sits outside <see cref="SampleBaseException" />.</summary>
public sealed class SampleUnrelatedException : Exception
{
    /// <summary>Initializes a new non-compliant sample failure.</summary>
    public SampleUnrelatedException()
        : base("outside")
    {
    }
}

/// <summary>A sample exception outside <see cref="SampleBaseException" /> that no other assembly can see.</summary>
internal sealed class SampleInternalException : Exception
{
    /// <summary>Initializes a new internal sample failure.</summary>
    public SampleInternalException()
        : base("internal")
    {
    }
}

/// <summary>A sample abstract exception outside <see cref="SampleBaseException" />, which nothing can throw.</summary>
public abstract class SampleAbstractException : Exception
{
    /// <summary>Initializes a new abstract sample failure.</summary>
    protected SampleAbstractException()
        : base("abstract")
    {
    }
}
