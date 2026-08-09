// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Chat;

/// <summary>Supplies the chat plan a new operation runs against.</summary>
/// <remarks>
/// <para>
/// The declaration behind a plan is reloadable, so the plan an operation runs against is a value with a publication
/// time rather than a constant of the process. Reading it here is what lets a corrected model reach the next question
/// instead of the next restart.
/// </para>
/// <para>
/// Read once per operation and use that instance for its duration. A run that re-read this mid-flight could answer half
/// a question with one model and half with another, which is the one thing the single-endpoint rule exists to prevent —
/// so a run resolves the plan from its own scope and the plan is what it holds.
/// </para>
/// <para>
/// Declared here and implemented by the composition root, for the reason
/// <see cref="Providers.IProviderEndpointCredentialSource" /> is: binding configuration, validating a
/// reloaded candidate, and deciding when one becomes current all belong to the host, and nothing in this boundary knows
/// what a configuration section is.
/// </para>
/// </remarks>
public interface IChatGenerationPlanSource
{
    /// <summary>Gets the plan built from the most recent declaration proven usable.</summary>
    ChatGenerationPlan Current { get; }
}
