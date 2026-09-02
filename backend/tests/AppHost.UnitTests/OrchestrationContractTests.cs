// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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

    [Fact]
    public void RunsIntegrationTests_IntegrationArgumentIsPresent_SelectsTheEphemeralTopology()
    {
        // Act
        var runsIntegrationTests = OrchestrationContract.RunsIntegrationTests(
            ["unrelated=value", OrchestrationContract.IntegrationTestingArgument]);

        // Assert
        Assert.True(runsIntegrationTests);
    }

    [Fact]
    public void RunsIntegrationTests_IntegrationArgumentIsAbsent_SelectsTheDevelopmentTopology()
    {
        // Act
        var runsIntegrationTests = OrchestrationContract.RunsIntegrationTests(["IntegrationTesting=false"]);

        // Assert
        Assert.False(runsIntegrationTests);
    }

    [Fact]
    public void ResolveOpenSslConfigurationPath_NormalTopologyWithNoOverride_UsesTheShippedDevelopmentPolicy()
    {
        // Arrange
        var appHostBaseDirectory = Path.Combine("apphost", "output");

        // Act
        var configurationPath = OrchestrationContract.ResolveOpenSslConfigurationPath(
            runsIntegrationTests: false,
            explicitlyConfiguredPath: null,
            appHostBaseDirectory);

        // Assert
        Assert.Equal(
            Path.Combine(appHostBaseDirectory, OrchestrationContract.DevelopmentOpenSslConfigurationFileName),
            configurationPath);
    }

    [Fact]
    public void ResolveOpenSslConfigurationPath_NormalTopologyWithOverride_UsesTheExplicitPath()
    {
        // Arrange
        const string explicitlyConfiguredPath = "/etc/mailfathom/custom-openssl.cnf";

        // Act
        var configurationPath = OrchestrationContract.ResolveOpenSslConfigurationPath(
            runsIntegrationTests: false,
            explicitlyConfiguredPath,
            appHostBaseDirectory: "unused");

        // Assert
        Assert.Equal(explicitlyConfiguredPath, configurationPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/etc/mailfathom/custom-openssl.cnf")]
    public void ResolveOpenSslConfigurationPath_IntegrationTopology_LeavesOpenSslUnconfigured(
        string? explicitlyConfiguredPath)
    {
        // Act
        var configurationPath = OrchestrationContract.ResolveOpenSslConfigurationPath(
            runsIntegrationTests: true,
            explicitlyConfiguredPath,
            appHostBaseDirectory: "unused");

        // Assert
        Assert.Null(configurationPath);
    }

    [Fact]
    public void DevelopmentHostEnvironment_NormalTopology_EnablesTheLocalSurfacesAndMailbox()
    {
        // Act
        var environment = OrchestrationContract.DevelopmentHostEnvironment;

        // Assert
        Assert.Equal(10, environment.Count);
        Assert.Equal("true", environment["MailSynchronization__Enabled"]);
        Assert.Equal("local", environment["MailSynchronization__Accounts__0__AccountId"]);
        Assert.Equal("Local mailbox", environment["MailSynchronization__Accounts__0__DisplayName"]);
        Assert.Equal("local-mail-password", environment["MailSynchronization__Accounts__0__Secrets__Password__Name"]);
        Assert.Equal("true", environment["McpEndpoint__Enabled"]);
        Assert.Equal(OrchestrationContract.DeveloperLoopbackAddress, environment["McpEndpoint__BindAddress"]);
        Assert.Equal("true", environment["AdminEndpoint__Enabled"]);
        Assert.Equal(OrchestrationContract.DeveloperLoopbackAddress, environment["AdminEndpoint__BindAddress"]);
        Assert.Equal("true", environment["ClientEndpoint__Enabled"]);
        Assert.Equal("password", environment["ClientEndpoint__Authentication__0__Method"]);
    }

    [Fact]
    public void DevelopmentBasicCredential_NormalTopology_UsesTheDocumentedSyntheticValues()
    {
        // Assert
        Assert.Equal("test", OrchestrationContract.DevelopmentBasicUsername);
        Assert.Equal("test-password", OrchestrationContract.DevelopmentBasicPassword);
    }

    /// <summary>The address the client and the service are wired together on is one nothing outside this machine reaches.</summary>
    /// <remarks>
    /// The one property of that constant the compiler cannot state. Four things are built from it — the sockets the
    /// host binds, the socket the client's development server binds, the endpoint Aspire publishes for it, and the
    /// address the client is handed — so a value that stopped being loopback would serve a development build to every
    /// interface of a developer's machine without any of the four disagreeing about it. CORS is not the fifth: the
    /// local topology leaves <c>ClientEndpoint:Cors:AllowedOrigins</c> unstated, which is the product default of every
    /// origin.
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

    /// <summary>
    /// The product default of every origin is what a client served from this machine needs, whichever loopback
    /// spelling its address carries. Writing one origin here is what made a preflight from the other spelling look
    /// like an empty mailbox.
    /// </summary>
    [Fact]
    public void Program_TheNormalClientTopology_LeavesTheClientCorsOriginsUnstated()
    {
        // Arrange
        var program = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Build", "Program.cs"));

        // Act, Assert
        Assert.DoesNotContain("ClientEndpoint__Cors__AllowedOrigins", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AdminEndpoint__Cors__AllowedOrigins", program, StringComparison.Ordinal);
    }

    /// <summary>One command brings up a working MailFathom, so a checkout that states nothing gets the client.</summary>
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

    /// <summary>A machine without the client's toolchain leaves it out of every run, which is what the key is for.</summary>
    [Theory]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData(" false ", false)]
    [InlineData("true", true)]
    public void ResolveClientEnabled_ValueStated_IsTheAnswerTheDeveloperWrote(string statedValue, bool expected)
    {
        // Act
        var clientEnabled = OrchestrationContract.ResolveClientEnabled(statedValue);

        // Assert
        Assert.Equal(expected, clientEnabled);
    }

    /// <summary>A developer who wrote something else asked for the client to stay out, so the run says why rather than starting it.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("no")]
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

    /// <summary>The address the client is handed is the surface's own origin, and the client appends its route prefix to it.</summary>
    /// <remarks>
    /// The trailing separator is the whole of what this establishes and nothing states it twice: the client composes a
    /// request as the base address followed by <c>/api/client</c>, so a value ending in one would ask for a path the
    /// surface does not serve — and the refusal would arrive as a route that does not exist rather than as an address
    /// that was composed wrongly.
    /// </remarks>
    [Fact]
    public void ResolveDevelopmentServiceAddress_TheClientSurfacePort_IsALoopbackOriginWithNoTrailingSeparator()
    {
        // Act
        var address = OrchestrationContract.ResolveDevelopmentServiceAddress(8082);

        // Assert
        var parsed = new Uri(address, UriKind.Absolute);

        Assert.Equal(OrchestrationContract.DeveloperLoopbackAddress, parsed.Host, StringComparer.Ordinal);
        Assert.Equal(8082, parsed.Port);
        Assert.Equal(Uri.UriSchemeHttp, parsed.Scheme, StringComparer.Ordinal);
        Assert.False(address.EndsWith('/'));
    }

    /// <summary>The variable is one Vite will actually hand to the page rather than one it withholds.</summary>
    /// <remarks>
    /// Vite exposes only the prefixed part of its process environment on <c>import.meta.env</c>, deliberately, so a
    /// name that lost the prefix would be written by the app model, carried by the development server, and read by
    /// nothing — a client that reported no service rather than a run that failed.
    /// </remarks>
    [Fact]
    public void ClientServiceAddressVariable_IsPrefixedSoTheDevelopmentServerExposesIt()
    {
        // Assert
        Assert.StartsWith("VITE_", OrchestrationContract.ClientServiceAddressVariable, StringComparison.Ordinal);
    }

    /// <summary>The client is started from the workspace root rather than from a package inside it, and by path alone.</summary>
    /// <remarks>
    /// A relative path is what keeps the two stacks apart: the app model holds a directory it starts a process in, so
    /// an absolute one taken from a machine, or a path that reached inside a package, would be the point at which the
    /// boundary stopped being a directory and a command.
    /// </remarks>
    [Fact]
    public void ClientWorkspaceDirectory_IsTheWorkspaceRootStatedRelativeToTheAppHost()
    {
        // Act
        var directory = OrchestrationContract.ClientWorkspaceDirectory;

        // Assert
        Assert.False(Path.IsPathRooted(directory));
        Assert.EndsWith("/frontend", directory, StringComparison.Ordinal);
    }

    /// <summary>The client is composed once, in the topology it belongs to, so a suite run starts no development server.</summary>
    /// <remarks>
    /// The integration suite starts this same app model, and the resource is added inside the branch that run does not
    /// take. Nothing in the type system says so, and a second block copied into the ephemeral branch would install a
    /// package graph on every suite run that no test reads — so what is asserted is that exactly one block composes it
    /// and exactly one reads the switch in front of it.
    /// </remarks>
    [Fact]
    public void Program_TheClientResource_IsComposedOnceBehindTheSwitchThatSelectsIt()
    {
        // Arrange
        var program = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Build", "Program.cs"));

        // Act
        var resources = program.Split(nameof(OrchestrationContract.ClientResourceName)).Length - 1;
        var switches = program.Split(nameof(OrchestrationContract.ClientEnabledKey)).Length - 1;

        // Assert
        Assert.Equal(1, resources);
        Assert.Equal(1, switches);
    }

    private static string IdentifierOf(string prefix) =>
        prefix[(OrchestrationContract.EphemeralResourceNamePrefix.Length + GeneratedIdentifierSeparator.Length)..];
}
