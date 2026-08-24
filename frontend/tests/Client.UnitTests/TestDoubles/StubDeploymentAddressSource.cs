// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Deployment;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>A head's own answer to where its deployment is, scripted, and recording whether it was asked at all.</summary>
/// <remarks>Whether it was asked is the assertion that matters: an address the build stated has to be taken instead of the head's answer rather than beside it, and a source that answered anyway would be a head reaching two deployments depending on which value won.</remarks>
internal sealed class StubDeploymentAddressSource : IDeploymentAddressSource
{
    /// <summary>The address this stub answers with wherever it is asked.</summary>
    internal static readonly Uri HeadsOwnAnswer = new("https://the-head-answered-for-itself.test/");

    /// <summary>Gets whether the head was asked to answer for itself.</summary>
    internal bool WasAsked { get; private set; }

    /// <inheritdoc />
    public Uri Resolve(DeploymentSettings settings)
    {
        this.WasAsked = true;

        return HeadsOwnAnswer;
    }
}
