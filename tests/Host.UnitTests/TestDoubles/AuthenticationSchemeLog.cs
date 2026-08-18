// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Every authentication scheme the pipeline asked to judge a request, in the order it asked.</summary>
/// <remarks>
/// Which schemes a request reaches is the whole of what keeps two surfaces apart, and it is invisible in a response:
/// an administrative credential compared against the protocol endpoint's keys is refused exactly as an absent one is.
/// Recording the question rather than the answer is what lets a test assert that the credential was never offered.
/// </remarks>
internal sealed class AuthenticationSchemeLog
{
    private readonly List<string> asked = [];

    /// <summary>Gets the schemes asked so far, oldest first.</summary>
    internal IReadOnlyList<string> Asked
    {
        get
        {
            lock (this.asked)
            {
                return [.. this.asked];
            }
        }
    }

    /// <summary>Records that a scheme was asked to authenticate a request.</summary>
    /// <param name="schemeName">The scheme the pipeline named, or <see langword="null" /> where it asked for the application default.</param>
    internal void Record(string? schemeName)
    {
        lock (this.asked)
        {
            this.asked.Add(schemeName ?? "(default)");
        }
    }
}
