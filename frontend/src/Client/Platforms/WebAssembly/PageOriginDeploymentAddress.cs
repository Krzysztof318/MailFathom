// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.InteropServices.JavaScript;
using MailFathom.Client.Backend;
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
/// A person's own choice is a different thing, and it does win over this. A bundle served from somewhere that is not a
/// deployment — a static host, a file server, a page kept open from one — has an origin that answers nothing, and that
/// origin being wrong is exactly what they would be correcting. So this is what the head knows and what a first visit
/// takes, rather than the last word; reaching a deployment that did not serve the page needs that deployment to permit
/// this origin as a cross-origin caller, which is the operator's decision and not one this can arrange.
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
    /// <remarks>
    /// <para>
    /// An origin and not a document, which is what the address every route is resolved against has to be —
    /// <see cref="DeploymentAddressRule" /> refuses one carrying anything more.
    /// </para>
    /// <para>
    /// The origin is judged here rather than handed on unjudged, and that is what keeps the loud half of
    /// <see cref="DeploymentChoice.RestoreAsync" /> loud. An address a person or a build <em>stated</em> and that this
    /// client may not be pointed at is a mistake somebody can go and correct, so it fails while the application is
    /// starting and names itself. An origin is neither: nobody wrote it, and a bundle served over clear text from
    /// something that is not this machine — a page opened from a colleague's development server, a static host on a
    /// local network — would take the whole application down at launch over a fact its reader could do nothing about.
    /// So an origin this client may not carry a credential to is an origin this head has no answer from, and the
    /// person is asked instead, which is the one thing that can actually resolve it.
    /// </para>
    /// </remarks>
    public Uri? Resolve(DeploymentSettings settings) =>
        Uri.TryCreate(Origin(), UriKind.Absolute, out var origin)
        && DeploymentAddressRule.Judge(origin) == DeploymentAddressRefusal.None
            ? origin
            : null;

    [JSImport("globalThis.mailFathomDeployment.origin")]
    private static partial string Origin();
}
