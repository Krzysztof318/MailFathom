// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Chat;

/// <summary>Where a chat request is sent and what it is routed to.</summary>
/// <param name="Alias">The deployment's own name for this endpoint, which is what a log, a metric, a resilience circuit, and a failure message call it.</param>
/// <param name="Address">The base address requests are sent to, or <see langword="null" /> for the provider's own default.</param>
/// <param name="RoutedModelName">What is sent as the model of a request, which for a cloud deployment is the deployment's name rather than the vendor's model identifier.</param>
/// <remarks>
/// <para>
/// The alias exists because the other two members are an address and a routing name, and an address may not be written
/// down: it identifies a tenant and a resource, so a failure that named one would put it in every log line. The alias is
/// a name the operator chose, which is exactly what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0003-first-party-exception-hierarchy-and-stable-error-codes.md">ADR 0003</see>
/// permits a message to carry. It is unique across every AI endpoint the deployment declares, embedding endpoints
/// included, because a credential, a circuit, and a log line are all keyed by it.
/// </para>
/// <para>
/// There is no vendor and no model identity beside the routed name, and that is the difference from an embedding
/// endpoint rather than an omission. A vector is stored and later compared against other vectors, so which model
/// produced it has to be recorded and proved; an answer is produced, presented, and gone, so nothing downstream ever
/// has to ask which model wrote it.
/// </para>
/// </remarks>
public sealed record ChatEndpoint(string Alias, Uri? Address, string RoutedModelName);
