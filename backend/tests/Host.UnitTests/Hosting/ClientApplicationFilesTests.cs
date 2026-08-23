// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting;

/// <summary>Covers how a deployment is recognized as carrying a client to serve.</summary>
/// <remarks>
/// The bundle is copied into the container image at build time and by nothing at run time, so a host started from
/// anything else has the directory and none of the files. Recognizing that by the entry document is what turns an
/// enabled setting nobody can use into a refusal at startup rather than a page of 404s, and the question is asked of
/// the environment's own file provider so that nothing here reads a disk.
/// </remarks>
public sealed class ClientApplicationFilesTests
{
    [Fact]
    public void BundleIsPresent_AnEnvironmentWhoseWebRootCarriesTheEntryDocument_ReportsTheBundle()
    {
        // Arrange
        var environment = EnvironmentServing(ClientApplicationOptions.EntryDocument, present: true);

        // Act
        var present = ClientApplicationFiles.BundleIsPresent(environment);

        // Assert
        Assert.True(present);
    }

    /// <summary>The directory an image without a client still has, which is why its existence proves nothing.</summary>
    [Fact]
    public void BundleIsPresent_AnEnvironmentWhoseWebRootIsEmpty_ReportsNoBundle()
    {
        // Arrange
        var environment = EnvironmentServing(ClientApplicationOptions.EntryDocument, present: false);

        // Act
        var present = ClientApplicationFiles.BundleIsPresent(environment);

        // Assert
        Assert.False(present);
    }

    private static IWebHostEnvironment EnvironmentServing(string entryDocument, bool present)
    {
        var file = Substitute.For<IFileInfo>();

        file.Exists.Returns(present);

        var files = Substitute.For<IFileProvider>();

        files.GetFileInfo(entryDocument).Returns(file);

        var environment = Substitute.For<IWebHostEnvironment>();

        environment.WebRootFileProvider.Returns(files);

        return environment;
    }
}
