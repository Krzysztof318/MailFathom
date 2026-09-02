// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using Microsoft.AspNetCore.StaticFiles;

namespace MailFathom.Host.Hosting;

/// <summary>Serves the client's bundle from the same origin as the surface it calls.</summary>
/// <remarks>
/// <para>
/// The whole of what the composition root gains for the client, and deliberately no more. The bundle is a directory of
/// files the container image carries; this puts the static-file middleware in front of the listeners the client surface
/// is served on and nothing else. No project here references one under <c>frontend/</c>, no type crosses between the
/// two stacks, and the only thing the service knows about the client is that some files were copied in beside it.
/// </para>
/// <para>
/// It is branched on the port rather than added to the application, because a deployment may serve the client surface
/// on a socket of its own. Routing matches a path and not a socket, so an unbranched static-file middleware would serve
/// the page wherever the process listens — including a port an operator gave the MCP surface precisely so that a
/// browser would never reach it. The branch is the whole of what confines the bundle, which is why it is composed ahead
/// of <see cref="Startup.SurfaceIsolation" /> rather than admitted by it: the bundle's paths are named by the client's
/// build, so a rule stated here would have to admit whatever that surface does not claim, and that remainder belongs to
/// the MCP endpoint.
/// </para>
/// </remarks>
internal static class ClientApplicationFiles
{
    /// <summary>The extensions a WebAssembly bundle carries that the platform's own content-type map does not name.</summary>
    /// <remarks>
    /// A file whose type nothing names is not served at all — <see cref="StaticFileOptions.ServeUnknownFileTypes" /> is
    /// left off, because guessing a type for anything that happens to be in the directory is how a static-file
    /// middleware serves something it should not. So each of these is here for a file the runtime actually fetches:
    /// <c>.dat</c> is the globalization data a .NET WebAssembly bundle reads to fold case and decode headers outside one
    /// alphabet, and <c>.blat</c> and <c>.dat</c> together are what the .NET WebAssembly runtime ships its ICU and
    /// timezone data as. An absent mapping arrives as a page that loads and then fails on the first message from
    /// outside ASCII, which is exactly the kind of failure worth spending three lines to avoid.
    /// </remarks>
    private static readonly (string Extension, string ContentType)[] BundleContentTypes =
    [
        (".blat", "application/octet-stream"),
        (".dat", "application/octet-stream"),
    ];

    /// <summary>Reports whether this deployment's files actually carry a client to serve.</summary>
    /// <param name="environment">The hosting environment, whose web root is where the image copies the bundle.</param>
    /// <returns><see langword="true" /> when the bundle's entry document is there.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="environment" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Asked through the environment's own file provider rather than of the file system, so a host composed in a test
    /// answers from whatever provider it was given. The entry document is what a present bundle is recognized by: the
    /// directory exists in an image built without one, and a deployment that enabled the client would otherwise learn
    /// about it from a page of 404s rather than from a refusal naming the setting.
    /// </remarks>
    internal static bool BundleIsPresent(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return environment.WebRootFileProvider.GetFileInfo(ClientApplicationOptions.EntryDocument).Exists;
    }

    /// <summary>Serves the bundle on the listeners the client surface is served on.</summary>
    /// <param name="app">The application pipeline being composed.</param>
    /// <param name="clientListenerPorts">The ports the client surface answers on.</param>
    /// <returns>The same application instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The entry document answers the root of those listeners and every other file answers its own path. There is no
    /// fallback mapping an unmatched path onto the entry document: the head navigates inside one document rather than
    /// by address, so a fallback would only turn a mistyped route on a shared socket into a page that loads and reports
    /// nothing, and on a socket shared with the MCP surface it would answer for that surface's own unmatched paths.
    /// </para>
    /// <para>
    /// Nothing here authenticates. The bundle is what a browser has to hold before it can present a credential at all,
    /// so requiring one to fetch it would be a client that can never sign in; what the page then calls is judged by the
    /// endpoint's own authorization exactly as any other caller is.
    /// </para>
    /// </remarks>
    internal static IApplicationBuilder UseClientApplication(
        this IApplicationBuilder app,
        IReadOnlySet<int> clientListenerPorts)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(clientListenerPorts);

        var contentTypes = new FileExtensionContentTypeProvider();

        foreach (var (extension, contentType) in BundleContentTypes)
        {
            contentTypes.Mappings[extension] = contentType;
        }

        var defaultDocuments = new DefaultFilesOptions();

        defaultDocuments.DefaultFileNames.Clear();
        defaultDocuments.DefaultFileNames.Add(ClientApplicationOptions.EntryDocument);

        return app.UseWhen(
            context => clientListenerPorts.Contains(context.Connection.LocalPort),
            clientListener => clientListener
                .UseDefaultFiles(defaultDocuments)
                .UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypes }));
    }
}
