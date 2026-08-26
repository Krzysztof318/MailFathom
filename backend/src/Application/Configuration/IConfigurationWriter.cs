// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Configuration;

/// <summary>Persists a change to the deployment's settings, having proved the configuration it would produce.</summary>
/// <remarks>
/// <para>
/// This is the one way MailFathom writes a setting, and the reason it is a port rather than an assignment is that
/// assigning is not what a write is. It resolves where the path is persisted, applies the changes to a candidate
/// document, composes the complete effective configuration the deployment would then read — including the sources that
/// outrank the persisted layer — and runs the same strict binding and the same validators a start runs, against that
/// candidate. Only a candidate that survives all of it is committed, and the commit is refused outright if the
/// document moved on while it was being judged.
/// </para>
/// <para>
/// What the caller supplies with the changes is the version they were authored against. Two administrators editing at
/// once is the case that decides the contract: the second write has to be refused rather than composed over the
/// first, because the two edits were each written against a document the other did not see and no later reader could
/// recover which of them was meant. A refused caller re-reads the version now in force and decides again.
/// </para>
/// <para>
/// Nothing here writes a file, an environment variable, a provider dictionary, or an options cache. The change lands
/// in PostgreSQL, which is where the persisted configuration layer is read from, and the layer republishes itself once
/// the commit is durable — so a reload token rises over a version that is already the deployment's, never over one a
/// later failure could take back.
/// </para>
/// </remarks>
public interface IConfigurationWriter
{
    /// <summary>Applies changes to the deployment's persisted configuration.</summary>
    /// <param name="edits">The changes, applied together or not at all, in the order given.</param>
    /// <param name="expectedVersion">The version the changes were authored against, which the commit is accepted against.</param>
    /// <param name="cancellationToken">Cancels the read, the validation, and the commit, and a write cancelled before the commit leaves the deployment's configuration unchanged. It does not cancel the republish that follows a durable commit, which is MailFathom's own to finish rather than the caller's to abandon.</param>
    /// <returns>The version the write committed, or the reason the deployment's settings are unchanged.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="edits" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="edits" /> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expectedVersion" /> is negative.</exception>
    Task<ConfigurationWriteResult> WriteAsync(
        IReadOnlyList<ConfigurationEdit> edits,
        long expectedVersion,
        CancellationToken cancellationToken);
}
