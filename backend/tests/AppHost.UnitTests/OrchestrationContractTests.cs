// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Sockets;
using Xunit;

namespace MailFathom.AppHost.UnitTests;

/// <summary>Covers the values the app model resolves rather than states outright: the ephemeral resource name, a pinned port, and the development data-encryption key.</summary>
/// <remarks>
/// What the naming assertions establish is what the removal at the end of a run depends on. A prefix that did not start
/// with the shared part would leave a run's resources outside every filter that finds them, and an identifier a
/// container runtime refuses would fail the run at a point that says nothing about why.
/// <para>
/// What the port assertions establish is that the opt-in behaves the way the app model's default depends on. An unset
/// key has to resolve to nothing, because that is what leaves Aspire to allocate and lets a second checkout run at the
/// same time; a stated one has to reach the endpoint unchanged, because a developer pins a port to stop having to look
/// it up.
/// </para>
/// <para>
/// The key assertion establishes something the compiler cannot: the constant is written into a <c>plaintext:</c> secret
/// reference, so a mistyped character or a change to the text behind it would first be reported by a developer's own
/// orchestration failing its startup validation, long after the edit.
/// </para>
/// </remarks>
public sealed class OrchestrationContractTests
{
    private const string GeneratedIdentifierSeparator = "-";

    /// <summary>The key length AES-256 takes, stated here because this project compiles one source file and reaches no shared constant.</summary>
    private const int DataEncryptionKeySizeInBytes = 32;

    /// <summary>The configuration section every pinned port is stated under, restated here for the reason above.</summary>
    private const string PinnedPortSection = "Ports:";

    /// <summary>A run that states no identifier still names its resources apart from every other run's.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveEphemeralResourceNamePrefix_NoIdentifierStated_GeneratesOneUsableInAContainerName(string? runIdentifier)
    {
        // Act
        var prefix = OrchestrationContract.ResolveEphemeralResourceNamePrefix(runIdentifier);

        // Assert
        var identifier = IdentifierOf(prefix);

        Assert.StartsWith(
            OrchestrationContract.EphemeralResourceNamePrefix + GeneratedIdentifierSeparator,
            prefix,
            StringComparison.Ordinal);
        Assert.Equal(8, identifier.Length);
        Assert.All(identifier, character => Assert.True(
            char.IsAsciiDigit(character) || character is >= 'a' and <= 'f',
            $"'{character}' is not a lowercase hexadecimal character, so the name is not one a container runtime accepts."));
    }

    /// <summary>Two runs that state nothing collide over nothing, which is the whole point of generating an identifier.</summary>
    /// <remarks>
    /// The one assertion here that reads a random value rather than its shape. Four bytes make a repeat a
    /// one-in-four-billion event, so this is not the nondeterminism the unit-test policy excludes; uniqueness is the
    /// property the identifier exists for, and a test that never observed two values could not state it.
    /// </remarks>
    [Fact]
    public void ResolveEphemeralResourceNamePrefix_CalledTwiceWithNoIdentifier_ProducesDifferentPrefixes()
    {
        // Act
        var first = OrchestrationContract.ResolveEphemeralResourceNamePrefix(null);
        var second = OrchestrationContract.ResolveEphemeralResourceNamePrefix(null);

        // Assert
        Assert.NotEqual(first, second);
    }

    /// <summary>A caller that states an identifier gets that one, so it can remove exactly what the run created.</summary>
    [Theory]
    [InlineData("a1b2c3d4", "a1b2c3d4")]
    [InlineData("  a1b2c3d4  ", "a1b2c3d4")]
    [InlineData("a", "a")]
    [InlineData("0123456789abcdef", "0123456789abcdef")]
    public void ResolveEphemeralResourceNamePrefix_UsableIdentifierStated_UsesItAfterTheSharedPrefix(
        string runIdentifier,
        string expectedIdentifier)
    {
        // Act
        var prefix = OrchestrationContract.ResolveEphemeralResourceNamePrefix(runIdentifier);

        // Assert
        Assert.Equal(
            $"{OrchestrationContract.EphemeralResourceNamePrefix}{GeneratedIdentifierSeparator}{expectedIdentifier}",
            prefix);
    }

    /// <summary>An identifier a container name cannot carry is refused rather than replaced.</summary>
    /// <remarks>
    /// Replacing it would name the resources under something the caller never learned, so the removal at the end of the
    /// run would match nothing and report success over everything it leaked.
    /// </remarks>
    [Theory]
    [InlineData("Bad Id!")]
    [InlineData("ABC123")]
    [InlineData("a1b2-c3d4")]
    [InlineData("a1b2_c3d4")]
    [InlineData("a1b2.c3d4")]
    [InlineData("zażółć")]
    [InlineData("01234567890123456")]
    public void ResolveEphemeralResourceNamePrefix_UnusableIdentifierStated_FailsNamingTheVariable(string runIdentifier)
    {
        // Act
        var failure = Assert.Throws<InvalidOperationException>(
            () => OrchestrationContract.ResolveEphemeralResourceNamePrefix(runIdentifier));

        // Assert
        Assert.Contains(OrchestrationContract.EphemeralRunIdentifierVariable, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The development key has to be what the host's own startup validation accepts, or no local run seals anything.</summary>
    [Fact]
    public void DataEncryptionKeyMaterial_TheStatedDevelopmentKey_IsBase64OfExactlyTheAcceptedKeyLength()
    {
        // Act
        var decoded = Convert.FromBase64String(OrchestrationContract.DataEncryptionKeyMaterial);

        // Assert
        Assert.Equal(DataEncryptionKeySizeInBytes, decoded.Length);
    }

    /// <summary>The ring's active identifier has to name a key the ring configures, which locally means the one key there is.</summary>
    /// <remarks>
    /// The app model writes this constant into both <c>DataEncryption__ActiveKeyId</c> and
    /// <c>DataEncryption__Keys__0__KeyId</c>, so the two agreeing is what stops a local run from being refused for
    /// naming an active key nothing configures.
    /// </remarks>
    [Fact]
    public void DataEncryptionKeyId_TheStatedIdentifier_IsOneTheConfigurationValidatorAccepts()
    {
        // Assert — the accepted shape is an alphanumeric first character followed by up to 63 more of the same plus
        // `.`, `_`, and `-`. It is restated rather than shared because this project compiles one source file.
        Assert.Matches("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", OrchestrationContract.DataEncryptionKeyId);
    }

    /// <summary>Nothing stated leaves the port to the orchestration, which is what lets two checkouts run at once.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolvePinnedPort_NothingStated_LeavesThePortUnpinned(string? statedPort)
    {
        // Act
        var port = OrchestrationContract.ResolvePinnedPort(OrchestrationContract.PinnedMcpEndpointPortKey, statedPort);

        // Assert
        Assert.Null(port);
    }

    /// <summary>A stated port reaches the endpoint as the number it states, which is the whole of what pinning buys.</summary>
    [Theory]
    [InlineData("8080", 8080)]
    [InlineData("  8081  ", 8081)]
    [InlineData("1", 1)]
    [InlineData("65535", 65535)]
    public void ResolvePinnedPort_PortStated_ReturnsThatNumber(string statedPort, int expectedPort)
    {
        // Act
        var port = OrchestrationContract.ResolvePinnedPort(OrchestrationContract.PinnedMcpEndpointPortKey, statedPort);

        // Assert
        Assert.Equal(expectedPort, port);
    }

    /// <summary>A value that is not a port fails the run naming the key, rather than allocating one and saying nothing.</summary>
    /// <remarks>
    /// Falling back would answer a developer's request for one fixed address with a different address every run, and the
    /// key they mistyped is the only thing that says where to look.
    /// </remarks>
    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("+8080")]
    [InlineData("8080.0")]
    [InlineData("8080 8081")]
    [InlineData("eighty-eighty")]
    [InlineData("0x1F90")]
    public void ResolvePinnedPort_ValueIsNotAPort_FailsNamingTheKey(string statedPort)
    {
        // Act
        var failure = Assert.Throws<InvalidOperationException>(
            () => OrchestrationContract.ResolvePinnedPort(OrchestrationContract.PinnedPostgresPortKey, statedPort));

        // Assert
        Assert.Contains(OrchestrationContract.PinnedPostgresPortKey, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Each socket is pinned under a key of its own, which is what lets one be pinned while the others stay allocated.</summary>
    [Fact]
    public void PinnedPortKeys_TheSocketsTheOrdinaryTopologyPublishes_AreStatedSeparately()
    {
        // Act
        string[] keys =
        [
            OrchestrationContract.PinnedMcpEndpointPortKey,
            OrchestrationContract.PinnedHealthEndpointsPortKey,
            OrchestrationContract.PinnedPostgresPortKey,
            OrchestrationContract.PinnedClientEndpointPortKey,
            OrchestrationContract.PinnedClientPortKey,
        ];

        // Assert
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, key => Assert.StartsWith(PinnedPortSection, key, StringComparison.Ordinal));
    }

    /// <summary>A port nothing pinned is one the host can actually bind, which is the whole of what the run needs from it.</summary>
    /// <remarks>
    /// The orchestrator refuses an unproxied endpoint that states no port, so this is what stands in for its allocation.
    /// A value the operating system reported free and immediately refuses to bind would be no allocation at all.
    /// </remarks>
    [Fact]
    public void FindFreePorts_APortNothingPinned_IsOneAListenerCanBind()
    {
        // Act
        var ports = OrchestrationContract.FindFreePorts(1);

        // Assert
        var port = Assert.Single(ports);

        Assert.InRange(port, 1, 65535);

        using var listener = new TcpListener(IPAddress.Any, port);

        listener.Start();
    }

    /// <summary>The sockets one run serves get different ports, or the second listener would collide with the first.</summary>
    /// <remarks>
    /// The reason the count is a parameter rather than something the caller loops over: a port asked for on its own is
    /// released before the next is chosen, so nothing would stop the operating system offering the same one twice.
    /// </remarks>
    [Fact]
    public void FindFreePorts_TheSocketsOneRunServes_AreEachGivenADifferentPort()
    {
        // Act
        var ports = OrchestrationContract.FindFreePorts(16);

        // Assert
        Assert.Equal(16, ports.Length);
        Assert.Equal(ports.Length, ports.Distinct().Count());
    }

    /// <summary>A run pinning every socket it serves asks for nothing, which is a request rather than a mistake.</summary>
    [Fact]
    public void FindFreePorts_NoneNeeded_ReturnsNone()
    {
        // Act
        var ports = OrchestrationContract.FindFreePorts(0);

        // Assert
        Assert.Empty(ports);
    }

    /// <summary>Nothing stated starts the client, which is what makes one command bring up a MailFathom with a face on it.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveClientEnabled_NothingStated_StartsTheClient(string? statedValue)
    {
        // Act
        var clientEnabled = OrchestrationContract.ResolveClientEnabled(statedValue);

        // Assert
        Assert.True(clientEnabled);
    }

    /// <summary>A developer who stated a value gets it, which is how a machine without the WebAssembly workload runs the rest.</summary>
    [Theory]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("  false  ", false)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    public void ResolveClientEnabled_ValueStated_ReturnsIt(string statedValue, bool expectedClientEnabled)
    {
        // Act
        var clientEnabled = OrchestrationContract.ResolveClientEnabled(statedValue);

        // Assert
        Assert.Equal(expectedClientEnabled, clientEnabled);
    }

    /// <summary>A value that is not a boolean fails the run naming the key, rather than starting what it was asked not to.</summary>
    /// <remarks>
    /// The whole reason a developer states this key is that building a WebAssembly bundle costs them something, so
    /// reading an unrecognized value as the default would spend exactly what they were avoiding and say nothing.
    /// </remarks>
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("no")]
    [InlineData("yes")]
    [InlineData("off")]
    [InlineData("disabled")]
    public void ResolveClientEnabled_ValueIsNotABoolean_FailsNamingTheKey(string statedValue)
    {
        // Act
        var failure = Assert.Throws<InvalidOperationException>(
            () => OrchestrationContract.ResolveClientEnabled(statedValue));

        // Assert
        Assert.Contains(OrchestrationContract.ClientEnabledKey, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The address the client and the service are wired together on is one nothing outside this machine reaches.</summary>
    /// <remarks>
    /// The one property of that constant the compiler cannot state. Three things are built from it — the socket the
    /// client's development server binds, the browser origin the service is configured to answer, and the address the
    /// head is built to call — so a value that stopped being loopback would publish a Debug WebAssembly bundle to
    /// every interface of a developer's machine without any of the three disagreeing about it.
    /// </remarks>
    [Fact]
    public void DeveloperLoopbackAddress_IsAnAddressOnlyThisMachineReaches()
    {
        // Act
        var parsed = IPAddress.TryParse(OrchestrationContract.DeveloperLoopbackAddress, out var address);

        // Assert
        Assert.True(parsed);
        Assert.True(IPAddress.IsLoopback(address!));
    }

    /// <summary>The name the deployment address travels into the client's build under has to be one MSBuild will carry.</summary>
    /// <remarks>
    /// It is passed as <c>--property:&lt;name&gt;=&lt;value&gt;</c>, and MSBuild takes a property name to be a letter
    /// or an underscore followed by letters, digits, underscores, or hyphens. A name outside that is not refused: the
    /// command line is accepted, the property never comes into being, the condition guarding the item in
    /// <c>frontend/src/Client/Client.csproj</c> is false, and the head is built with no address at all — which arrives
    /// as a client calling its own development server rather than as anything the run said.
    /// </remarks>
    [Fact]
    public void ClientDeploymentAddressProperty_IsANameMsBuildAcceptsAsAProperty()
    {
        // Act
        var name = OrchestrationContract.ClientDeploymentAddressProperty;

        // Assert
        Assert.NotEmpty(name);
        Assert.True(char.IsAsciiLetter(name[0]) || name[0] == '_');
        Assert.All(name[1..], character => Assert.True(
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-',
            $"'{character}' is not a character an MSBuild property name may carry."));
    }

    private static string IdentifierOf(string prefix) =>
        prefix[(OrchestrationContract.EphemeralResourceNamePrefix.Length + GeneratedIdentifierSeparator.Length)..];
}
