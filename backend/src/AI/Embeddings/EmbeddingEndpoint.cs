// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;

namespace MailFathom.AI.Embeddings;

/// <summary>One route to a vector space: where to send a request, what to route it to, and how to authenticate.</summary>
/// <param name="Alias">The deployment's own name for this endpoint, which is what a log, a metric, and a failure message call it.</param>
/// <param name="Identity">The geometry this endpoint produces vectors in. Every endpoint of one chain declares the same one, and startup refuses a chain where they differ.</param>
/// <param name="Address">The base address requests are sent to, or <see langword="null" /> for the provider's own default.</param>
/// <param name="RoutedModelName">What is sent as the model of a request, which for a cloud deployment is the deployment's name rather than the vendor's model identifier.</param>
/// <param name="SupportsRequestedDimension">Whether the endpoint honours a requested vector width, so a narrower space is asked for rather than cut out of a wider answer.</param>
/// <remarks>
/// <para>
/// The alias exists because everything else here is either an address or a credential, and neither may be written down.
/// An address identifies a tenant and a resource, so a failure that named one would put it in every log line; the alias
/// is a name the operator chose, which is exactly what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0003-first-party-exception-hierarchy-and-stable-error-codes.md">ADR 0003</see>
/// permits a message to carry.
/// </para>
/// <para>
/// <paramref name="RoutedModelName" /> is separate from the identity's model identifier because the two answer
/// different questions. The identity says which model produced a vector and is what makes two vectors comparable;
/// the routed name says which string this endpoint recognizes, and a cloud deployment names that after itself. Keeping
/// them apart is what lets one vector space be reached through a first-party API and a cloud deployment of the same
/// model, which is the case a fallback chain exists for.
/// </para>
/// <para>
/// <paramref name="SupportsRequestedDimension" /> is declared rather than inferred from the model name, for the reason
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// gives for the model never being a compile-time constant: a table of which models accept the parameter would be a
/// list of model names in code, and it would be wrong the week after it was written.
/// </para>
/// </remarks>
public sealed record EmbeddingEndpoint(
    string Alias,
    EmbeddingProfileIdentity Identity,
    Uri? Address,
    string RoutedModelName,
    bool SupportsRequestedDimension);
