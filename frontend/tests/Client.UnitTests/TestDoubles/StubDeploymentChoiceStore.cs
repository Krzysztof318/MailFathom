// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Deployment;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>The platform store a choice outlives a restart in, standing in for one no test process has.</summary>
/// <remarks>Reading back what was written is what makes a restart assertable here: a test writes through the choice and reads the store, which is the same thing the next launch does.</remarks>
internal sealed class StubDeploymentChoiceStore : IDeploymentChoiceStore
{
    /// <summary>Initializes a store holding nothing, which is what a fresh installation has.</summary>
    internal StubDeploymentChoiceStore()
    {
    }

    /// <summary>Initializes a store already holding a choice, which is what a second launch reads.</summary>
    /// <param name="kept">What was chosen before.</param>
    internal StubDeploymentChoiceStore(Uri? kept) => this.Kept = kept;

    /// <summary>Gets what the store holds, which is what a later launch would read.</summary>
    internal Uri? Kept { get; private set; }

    /// <inheritdoc />
    public Uri? Read() => this.Kept;

    /// <inheritdoc />
    public void Write(Uri address) => this.Kept = address;
}
