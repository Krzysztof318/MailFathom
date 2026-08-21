// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;

namespace MailFathom.Host.Configuration.Embeddings;

/// <summary>The vector space this deployment declares it embeds with, which may be none.</summary>
/// <remarks>
/// <para>
/// A registration every container holds, unlike <see cref="AI.Embeddings.EmbeddingGenerationPlan" />, which
/// exists only where a chain was declared because an adapter with nothing to reach could not be constructed. The
/// administrative surface needs the opposite guarantee: an instance that declared no provider is precisely the one
/// whose operator is asking why semantic search is not working, and an endpoint that could not be resolved there would
/// answer that question with a container failure.
/// </para>
/// <para>
/// Only the geometry, because that is the whole of what a profile is. The endpoint addresses, the credentials, and the
/// bounds are on the plan and belong nowhere near a surface that reports what an instance is doing.
/// </para>
/// </remarks>
/// <param name="Identity">The declared geometry, or <see langword="null" /> where this deployment declared no provider.</param>
internal sealed record DeclaredEmbeddingGeometry(EmbeddingProfileIdentity? Identity);
