// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.InteropServices.JavaScript;
using MailFathom.Client.Deployment;

namespace MailFathom.Client.Platforms.WebAssembly;

/// <summary>The browser head's deployment address: the origin this document was served from.</summary>
/// <remarks>
/// <para>
/// A deployment that serves the client serves it from the same address its client surface answers on, which is what
/// makes this the whole of the browser head's configuration. There is nothing to state, nothing to keep in step with
/// the server, and no origin to get wrong — and no cross-origin request either, so no preflight stands between the page
/// and its first call.
/// </para>
/// <para>
/// What the installation stated is deliberately not read. An address written for an installed head would name some
/// deployment, and honouring it here would let a page served by one deployment talk to another — which is a
/// cross-origin call the browser would refuse anyway, arriving as a client that starts and then fails at every request
/// rather than as a page that reached whoever served it.
/// </para>
/// <para>
/// The one case this is the wrong answer to is a head an orchestration started, where the origin is a development
/// server and the service listens on another socket. That is not a second reading of this document — nothing here can
/// know it — and it is answered before this is asked, by
/// <see cref="BuildStatedDeploymentAddress" />.
/// </para>
/// <para>
/// This type is compiled into the browser head alone, as everything under <c>Platforms/WebAssembly/</c> is, which is
/// what lets it use the JavaScript interop no other head has a counterpart for.
/// </para>
/// </remarks>
internal sealed partial class PageOriginDeploymentAddress : IDeploymentAddressSource
{
    /// <inheritdoc />
    /// <remarks>The origin carries a trailing slash, so it is an origin and not a document: the address every route is resolved against has to be a base, and <c>Client.Backend</c> refuses one carrying anything more than that.</remarks>
    public Uri Resolve(DeploymentSettings settings) => new(Origin());

    [JSImport("globalThis.mailFathomDeployment.origin")]
    private static partial string Origin();
}
