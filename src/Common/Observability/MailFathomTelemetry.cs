// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Common.Observability;

/// <summary>Names the activity source and meter MailFathom publishes to under its own name.</summary>
/// <remarks>
/// <para>
/// One name covers the whole application. A subsystem does not choose what it is called, and it does not get a name of
/// its own until there is something to tell apart: what an operator wants first is every signal this process owns and
/// no signal a library emits, and one name answers that exactly. Which subsystem a span or an instrument came from is
/// carried by its own name and its tags, which is where a distinction can be added without a second registration
/// having to exist before anybody needs it.
/// </para>
/// <para>
/// The same string serves both registries. An activity source and a meter are separate subscriptions to OpenTelemetry
/// and cannot collide, so publishing spans and instruments under one name is a simplification rather than a conflict.
/// The name is declared here ahead of the code that publishes to it, which is the whole point of the type: a boundary
/// reads what it is called instead of inventing it.
/// </para>
/// <para>
/// A span or an instrument published under this name carries counts, sizes, durations, outcomes, error codes, and
/// MailFathom's own configured account and folder aliases. It never carries mail content, an address, a subject, a
/// remote folder path, a message identifier, a UID, a search term, a credential, or model prompt and completion text —
/// which is a cardinality rule as much as a privacy one, because every one of those would open a time series per
/// message or per person.
/// </para>
/// <para>
/// What this declares is the name and nothing else. How a publisher obtains its meter and its activity source, and
/// which of them it disposes, stays with the publisher, because that follows from the lifetime it is registered with
/// rather than from what it is called.
/// </para>
/// </remarks>
public static class MailFathomTelemetry
{
    /// <summary>The activity source and meter name every MailFathom signal is published under.</summary>
    public const string Name = "MailFathom";
}
