// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MailFathom.Common.Observability;

/// <summary>The activity source and meter MailFathom publishes every signal of its own through.</summary>
/// <remarks>
/// <para>
/// One name covers the whole application, and one instance of each registry carries it. A subsystem does not choose
/// what it is called and does not create a source or a meter of its own: it publishes through the two members below,
/// which is what makes a name invented for a feature impossible rather than merely discouraged. What an operator wants
/// first is every signal this process owns and no signal a library emits, and one name answers that exactly; which
/// subsystem a signal came from is carried by the span or instrument name and its tags, where a distinction can be
/// added without a second registration having to exist before anybody needs it.
/// </para>
/// <para>
/// The same string serves both registries. An activity source and a meter are separate subscriptions to OpenTelemetry
/// and cannot collide, so publishing spans and instruments under one name is a simplification rather than a conflict.
/// </para>
/// <para>
/// Both live for the lifetime of the process and are deliberately not disposed. A publisher holds them rather than
/// owning them, so a type that reports through one implements no disposal on their account — disposing a shared source
/// would silence every other publisher, and nothing is reclaimed by disposing either at shutdown.
/// </para>
/// <para>
/// A span or an instrument published here carries counts, sizes, durations, outcomes, error codes, and MailFathom's
/// own configured account and folder aliases. It never carries mail content, an address, a subject, a remote folder
/// path, a message identifier, a UID, a search term, a credential, or model prompt and completion text — which is a
/// cardinality rule as much as a privacy one, because every one of those would open a time series per message or per
/// person.
/// </para>
/// <para>
/// A publisher choosing a word for one of its own dimensions writes it in <c>snake_case</c>, and writes an outcome as a
/// past participle: <c>succeeded</c>, <c>failed</c>, <c>cancelled</c>, <c>lease_lost</c>, <c>outcome_unknown</c>. This
/// is stated once because the cost of a second spelling is not local to the publisher that invents it — a family
/// writing <c>lease-lost</c> where another writes <c>lease_lost</c> is two words for one ending, and a panel written
/// against either one silently answers for half the deployment. The same rule is why one dimension keeps one key
/// wherever it is published: a folder is <c>mailfathom.mail.folder</c> in every family, so a query written against one
/// subsystem can be reused against the next.
/// </para>
/// <para>
/// It governs the words a publisher chooses, not the ones it carries in. A value that arrives already named — a domain
/// identity such as a mutation's own name, or a word a protocol contract publishes — is published exactly as it is
/// named there, because renaming it for a dashboard would put a second spelling into the world rather than remove one.
/// </para>
/// </remarks>
public static class Telemetry
{
    /// <summary>The activity source and meter name every MailFathom signal is published under.</summary>
    public const string Name = "MailFathom";

    /// <summary>Gets the activity source every MailFathom span is started from.</summary>
    public static ActivitySource ActivitySource { get; } = new(Name);

    /// <summary>Gets the meter every MailFathom instrument is created on.</summary>
    public static Meter Meter { get; } = new(Name);
}
