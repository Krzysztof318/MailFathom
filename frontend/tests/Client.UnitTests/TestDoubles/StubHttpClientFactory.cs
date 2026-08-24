// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>Hands out the transports a test scripted, by the names the registration gives them.</summary>
/// <remarks>
/// The seam the client and the sign-in now reach their transports through. They ask for one per exchange rather than
/// holding one, which is what lets a deployment address change while the application runs, so a test has to supply the
/// factory rather than the client — and asking twice has to yield something usable twice, which is why the same
/// instance comes back rather than a new one whose handler nothing owns.
/// </remarks>
internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly IReadOnlyDictionary<string, HttpClient> transports;

    /// <summary>Initializes the factory over the transports a test scripted.</summary>
    /// <param name="transports">Each transport, under the name the registration would have given it.</param>
    internal StubHttpClientFactory(IReadOnlyDictionary<string, HttpClient> transports) =>
        this.transports = transports;

    /// <summary>Gets the names that were asked for, in order.</summary>
    internal List<string> Asked { get; } = [];

    /// <inheritdoc />
    public HttpClient CreateClient(string name)
    {
        this.Asked.Add(name);

        return this.transports.TryGetValue(name, out var transport)
            ? transport
            : throw new InvalidOperationException($"No transport was scripted under the name '{name}'.");
    }
}
