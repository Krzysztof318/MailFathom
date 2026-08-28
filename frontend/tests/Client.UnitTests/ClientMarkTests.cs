// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Xml.Linq;

namespace MailFathom.Client.UnitTests;

/// <summary>
/// Reads <c>Client.csproj</c> and holds the application icon and the splash screen to the repository's own artwork.
/// </summary>
/// <remarks>
/// <para>
/// MailFathom is identified by one drawing, under <c>assets/</c>, and everything else that shows a mark points at it —
/// the README heading, the Helm chart, the container image's logo label, and the documentation site. What this suite
/// refuses is the client quietly acquiring a second one. That is not a hypothetical: the icon and the splash were a
/// pair of hand-drawn approximations of that artwork until the change this file arrived in, and nothing said so.
/// </para>
/// <para>
/// The project file is parsed rather than the generated renderings inspected, exactly as
/// <c>BuildStatedDeploymentAddressTests</c> parses it for the configuration key the build writes. A rendering is
/// produced by the Uno resizetizer from whatever it was pointed at, so it says the pointing succeeded and never says
/// that what it was pointed at was right.
/// </para>
/// </remarks>
public sealed class ClientMarkTests
{
    private const string ArtworkDirectory = "$(RepositoryRoot)assets/";

    private static readonly XDocument Project =
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Build", "Client.csproj"));

    /// <summary>The icon and the splash are each generated from a file under <c>assets/</c> and from nothing else.</summary>
    /// <remarks>
    /// The two ends of the staging are asserted together because they are one decision. The resizetizer turns a source
    /// file name into the resource name a head addresses, and refuses a name that is not already a valid identifier,
    /// so the build copies each file out of <c>assets/</c> under a name it accepts. A copy whose destination stopped
    /// matching the item that consumes it would leave the head rendering nothing rather than the wrong mark. The two
    /// are asserted to be different files as well, because the resizetizer refuses an icon and a splash that are the
    /// same one, and a single item of each because it answers a second by taking the first and warning.
    /// </remarks>
    [Fact]
    public void Include_TheIconAndTheSplash_AreEachStagedFromTheArtworkUnderAssets()
    {
        // Arrange
        var staged = Project
            .Descendants("Copy")
            .Where(copy => copy.Attribute("SourceFiles")?.Value.StartsWith(ArtworkDirectory, StringComparison.Ordinal) == true)
            .Select(copy => copy.Attribute("DestinationFiles")?.Value)
            .ToArray();

        // Act
        var icon = Assert.Single(Project.Descendants("UnoIcon").Select(element => element.Attribute("Include")?.Value));
        var splash = Assert.Single(Project.Descendants("UnoSplashScreen").Select(element => element.Attribute("Include")?.Value));

        // Assert
        Assert.Contains(icon, staged, StringComparer.Ordinal);
        Assert.Contains(splash, staged, StringComparer.Ordinal);
        Assert.NotEqual(icon, splash, StringComparer.Ordinal);
    }

    /// <summary>The splash screen's ground and the browser head's own background are one colour written in two files.</summary>
    /// <remarks>
    /// The resizetizer paints the ground behind the splash from the project file, and the browser paints the page
    /// behind the bundle from <c>background_color</c> in the web manifest before any of it has loaded. Two values that
    /// drifted apart would reach a person as the window changing colour on its way to the first frame, which is
    /// neither a build failure nor something either file can notice about itself.
    /// </remarks>
    [Fact]
    public void Color_TheSplashScreensGround_IsTheBackgroundTheWebManifestDeclares()
    {
        // Arrange
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Platforms", "manifest.webmanifest")));

        var declared = manifest.RootElement.GetProperty("background_color").GetString();

        // Act
        var ground = Project
            .Descendants("UnoSplashScreen")
            .Select(element => element.Attribute("Color")?.Value)
            .ToArray();

        // Assert
        Assert.Equal(declared, Assert.Single(ground), StringComparer.OrdinalIgnoreCase);
    }
}
