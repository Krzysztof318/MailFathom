// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.IntegrationTests.Mailbox;

/// <summary>Places one test within its class's mailbox-state sequence.</summary>
/// <param name="position">The position, ascending, with lower positions running first.</param>
/// <remarks>
/// Apply this only where a later test genuinely reads state an earlier one produced. Everywhere else the tests stay
/// order-independent, and adding a step to a test that does not need one hides that it could have run alone.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MailboxStateStepAttribute(int position) : Attribute
{
    /// <summary>Gets the position within the sequence.</summary>
    public int Position { get; } = position;
}
