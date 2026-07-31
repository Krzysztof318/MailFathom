// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Domain.Folders;

namespace MailMcp.Application.Folders;

/// <summary>States what happened when an alias was resolved against the folders a server advertises.</summary>
public enum MailFolderResolutionOutcome
{
    /// <summary>The alias names a remote folder the server advertises.</summary>
    Resolved = 0,

    /// <summary>No advertised folder matched the configured path or role, so this one alias cannot be synchronized.</summary>
    NoAdvertisedFolderMatched = 1,

    /// <summary>Several advertised folders carry the configured role, so which one the alias names is the operator's to state.</summary>
    AdvertisedFoldersAreAmbiguous = 2,
}

/// <summary>Carries the binding an alias resolved to, or the reason it resolved to none.</summary>
/// <remarks>
/// An unmatched alias is a result rather than an exception because it fails one folder and no more: the remaining
/// aliases of the same account are configured independently and stay synchronizable.
/// </remarks>
public sealed record MailFolderResolutionResult
{
    private MailFolderResolutionResult(MailFolderResolutionOutcome outcome, MailFolderResolution? resolution)
    {
        this.Outcome = outcome;
        this.Resolution = resolution;
    }

    /// <summary>Gets what happened.</summary>
    public MailFolderResolutionOutcome Outcome { get; }

    /// <summary>Gets the durable binding, which is present exactly when <see cref="Outcome" /> is <see cref="MailFolderResolutionOutcome.Resolved" />.</summary>
    public MailFolderResolution? Resolution { get; }

    /// <summary>Reports an alias that now has a durable binding.</summary>
    /// <param name="resolution">The binding the alias resolved to.</param>
    /// <returns>A resolved result.</returns>
    public static MailFolderResolutionResult Resolved(MailFolderResolution resolution) =>
        new(MailFolderResolutionOutcome.Resolved, resolution);

    /// <summary>Reports an alias no advertised folder matched.</summary>
    /// <returns>An unmatched result.</returns>
    public static MailFolderResolutionResult NoAdvertisedFolderMatched() =>
        new(MailFolderResolutionOutcome.NoAdvertisedFolderMatched, resolution: null);

    /// <summary>Reports a configured role that several advertised folders carry.</summary>
    /// <returns>An ambiguous result.</returns>
    public static MailFolderResolutionResult AdvertisedFoldersAreAmbiguous() =>
        new(MailFolderResolutionOutcome.AdvertisedFoldersAreAmbiguous, resolution: null);
}
