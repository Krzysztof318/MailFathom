// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent.Egress;

/// <summary>Names a point at which text crosses out of this deployment and is therefore scanned before it does.</summary>
/// <remarks>
/// <para>
/// <b>This enumeration is the register of guarded egress.</b> A prompt is the point everybody thinks of and the least
/// likely to be the leak: a credential reaches a third party just as completely through an embedding request or a tool
/// result, and those paths are written by people who are not thinking about redaction at the time. A path that hands
/// text to somebody else and is not a member here is unguarded, so adding one is part of adding the path rather than a
/// follow-up to it.
/// </para>
/// <para>
/// Three paths the design names carry no member, and each absence is a fact about this deployment rather than an
/// omission. <b>Logs</b> and <b>audit events</b> carry no message text at all — every event of both is composed from
/// identifiers, this deployment's own configured aliases, counts, and outcomes, which is a rule the whole repository is
/// written under and the reason a finding is recorded here by category and rule rather than by value. Recording what was
/// found, where, and in which message would recreate the leak inside the record written to prevent it. <b>Webhook
/// payloads</b> have no member because MailFathom sends no webhook; the day it does, the payload is composed from mail
/// and the member arrives with it.
/// </para>
/// <para>
/// A member is a metric tag rather than a stored value, so the numbers are free to be reassigned only in the sense every
/// enum here is: allocated once, in declaration order, and never reordered or reused.
/// </para>
/// </remarks>
public enum SensitiveContentEgressPoint
{
    /// <summary>Text composed into a request to a chat provider, including a retrieved extract and a tool result.</summary>
    ChatPrompt = 0,

    /// <summary>Text sent to an embedding provider, which is a configured endpoint whether or not it is inside the deployment.</summary>
    HostedEmbeddingInput = 1,

    /// <summary>Text an MCP tool returns: a search snippet, a subject a listing publishes, and an answer a run produced.</summary>
    McpSnippet = 2,
}
