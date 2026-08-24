// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Deployment;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Deployment;

/// <summary>Covers how a head reads the deployment the build that produced it stated.</summary>
/// <remarks>
/// Three claims. A stated address has to be taken instead of the head's own answer rather than beside it, because the
/// case it exists for is one where that answer is wrong — a browser head served by a development server would
/// otherwise call the file server it was fetched from. A build that stated nothing has to change nothing at all,
/// because every published artifact is one of those. And a value nothing can be reached at has to fail while the host
/// is being composed, naming what was written and where it came from, rather than arriving as a window that cannot
/// explain itself.
/// </remarks>
public sealed class BuildStatedDeploymentAddressTests
{
    private static readonly DeploymentSettings AnInstallationStatingNothing = new();

    [Fact]
    public void Resolve_TheBuildStatedAnAddress_IsTheAddressAndTheHeadIsNotAsked()
    {
        // Arrange
        var head = new StubDeploymentAddressSource();
        var source = new BuildStatedDeploymentAddress(head, "http://127.0.0.1:8080/");

        // Act
        var address = source.Resolve(AnInstallationStatingNothing);

        // Assert
        Assert.Equal(new Uri("http://127.0.0.1:8080/"), address);
        Assert.False(head.WasAsked);
    }

    /// <summary>Every artifact nobody pointed is this case, so it has to be the head's own answer and nothing else.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_TheBuildStatedNothing_IsWhateverTheHeadAnswers(string? stated)
    {
        // Arrange
        var head = new StubDeploymentAddressSource();
        var source = new BuildStatedDeploymentAddress(head, stated);

        // Act
        var address = source.Resolve(AnInstallationStatingNothing);

        // Assert
        Assert.Equal(StubDeploymentAddressSource.HeadsOwnAnswer, address);
        Assert.True(head.WasAsked);
    }

    /// <summary>A value with surrounding whitespace is an address rather than a build that stated nothing.</summary>
    [Fact]
    public void Resolve_TheBuildStatedAnAddressWithSurroundingWhitespace_IsTheAddress()
    {
        // Arrange
        var source = new BuildStatedDeploymentAddress(new StubDeploymentAddressSource(), "  https://mail.example.test/  ");

        // Act
        var address = source.Resolve(AnInstallationStatingNothing);

        // Assert
        Assert.Equal(new Uri("https://mail.example.test/"), address);
    }

    /// <summary>What a mistyped property has to arrive as: a failure naming the key and repeating what was written.</summary>
    /// <remarks>
    /// Only the shape no route resolves against is refused here. A value that parses into something no deployment
    /// could be reached at — a scheme this client does not speak, an address carrying more than an origin — is
    /// <c>Client.Backend</c>'s refusal and is asserted with it, so one rule judges every head's answer alike.
    /// </remarks>
    [Theory]
    [InlineData("ht!tp://mail.example.test")]
    [InlineData("api/client")]
    [InlineData("127.0.0.1:8080")]
    public void Resolve_TheBuildStatedSomethingThatIsNotAnAddress_FailsNamingTheKeyAndTheValue(string stated)
    {
        // Arrange
        var head = new StubDeploymentAddressSource();
        var source = new BuildStatedDeploymentAddress(head, stated);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => source.Resolve(AnInstallationStatingNothing));

        // Assert
        Assert.Contains(stated, failure.Message, StringComparison.Ordinal);
        Assert.Contains(BuildStatedDeploymentAddress.ConfigurationKey, failure.Message, StringComparison.Ordinal);
        Assert.False(head.WasAsked);
    }

    /// <summary>The head is what this wraps, so composing it without one is refused rather than deferred to a run that states no address.</summary>
    [Fact]
    public void Construct_NoHead_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new BuildStatedDeploymentAddress(null!, "https://mail.example.test/"));
    }

    /// <summary>The constructor a head actually composes reads the key the build writes, which is the whole of the channel.</summary>
    /// <remarks>
    /// It writes process-wide state, which no other test in this suite reads or writes, and puts it back afterwards.
    /// The alternative is asserting the mechanism nowhere: every other test here states the value directly, so a
    /// constructor reading the wrong key would pass all of them.
    /// </remarks>
    [Fact]
    public void Resolve_TheKeyTheBuildWrites_IsWhatTheComposedSourceReads()
    {
        // Arrange
        var head = new StubDeploymentAddressSource();

        AppContext.SetData(BuildStatedDeploymentAddress.ConfigurationKey, "http://127.0.0.1:8080/");

        try
        {
            var source = new BuildStatedDeploymentAddress(head);

            // Act
            var address = source.Resolve(AnInstallationStatingNothing);

            // Assert
            Assert.Equal(new Uri("http://127.0.0.1:8080/"), address);
            Assert.False(head.WasAsked);
        }
        finally
        {
            AppContext.SetData(BuildStatedDeploymentAddress.ConfigurationKey, null);
        }
    }
}
