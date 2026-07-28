// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Secrets;

/// <summary>Turns one secret-bearing configuration value into material.</summary>
/// <remarks>
/// <para>
/// The contract lives in <c>Infrastructure</c> and is invoked by the host during startup and by the adapters that need
/// material. It is deliberately unreachable from <c>Application</c> and <c>Domain</c>: a resolver visible there would
/// give every use case the ability to ask for any secret by name, which is the broad secret access
/// <see href="../../../docs/decisions/0002-configuration-reading-mapping-and-reload-boundary.md">ADR 0002</see>
/// forbids normalizing into application code. Application code receives only the narrowly scoped settings each operation needs.
/// </para>
/// <para>
/// Resolution is asynchronous and cancellable even though every scheme shipped today reads a local file or an
/// environment variable. A managed store is a network call, and retrofitting an asynchronous contract later would break
/// every consumer at the moment a first provider adapter is added.
/// </para>
/// </remarks>
public interface ISecretReferenceResolver
{
    /// <summary>Resolves one configured value into owned material.</summary>
    /// <param name="configuredValue">
    /// The bound value. It is a <c>&lt;scheme&gt;:&lt;target&gt;</c> reference under a reference-accepting
    /// <see cref="SecretValueInterpretation" />, and the material itself under an inline one.
    /// </param>
    /// <param name="cancellationToken">Cancels the retrieval, including at the file-system boundary.</param>
    /// <returns>The material, whose ownership passes to the caller, or a named failure.</returns>
    /// <remarks>Neither the returned failure nor anything derived from it may carry the target or the material.</remarks>
    Task<SecretResolutionResult> ResolveAsync(string? configuredValue, CancellationToken cancellationToken);
}
