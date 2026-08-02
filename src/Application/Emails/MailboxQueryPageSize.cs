// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Application.Emails;

/// <summary>How many emails one page of a mailbox query returns.</summary>
/// <remarks>
/// <para>
/// The type exists so the bound is stated once and enforced wherever a page is asked for, rather than re-checked by
/// every query use case. An unbounded page is the one thing a mailbox query never serves: the result is a projection of
/// personal data that crosses a protocol boundary, and its size is the control that keeps that bounded.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and carries a value of zero, which names no page.
/// <see cref="IsSpecified" /> reports that. Every page size a query uses arrives through <see cref="Create" /> or
/// <see cref="FromRequested" />, so the default cannot reach a query from a request.
/// </para>
/// </remarks>
public readonly record struct MailboxQueryPageSize
{
    /// <summary>The greatest page size a mailbox query serves, per the architecture draft.</summary>
    public const int MaximumValue = 100;

    /// <summary>The page size a request that names none receives.</summary>
    /// <remarks>Smaller than the maximum deliberately: a caller that has not thought about the size gets a page that costs little to produce and little to read.</remarks>
    public const int DefaultValue = 25;

    private MailboxQueryPageSize(int value) => this.Value = value;

    /// <summary>Gets the page size a request that names none receives.</summary>
    public static MailboxQueryPageSize Default { get; } = new(DefaultValue);

    /// <summary>Gets how many emails the page returns.</summary>
    public int Value { get; }

    /// <summary>Gets whether this value names a page size rather than the unusable struct default.</summary>
    public bool IsSpecified => this.Value is not 0;

    /// <summary>Creates a page size from what a request asked for.</summary>
    /// <param name="value">The requested page size.</param>
    /// <returns>The validated page size.</returns>
    /// <exception cref="MailboxQueryPageSizeOutOfRangeException">Thrown when <paramref name="value" /> is below one or above <see cref="MaximumValue" />.</exception>
    public static MailboxQueryPageSize Create(int value) => value is >= 1 and <= MaximumValue
        ? new MailboxQueryPageSize(value)
        : throw new MailboxQueryPageSizeOutOfRangeException(value, MaximumValue);

    /// <summary>Creates a page size from a request that may not have named one.</summary>
    /// <param name="value">The requested page size, or <see langword="null" /> when the request named none.</param>
    /// <returns>The validated page size, or <see cref="Default" /> when the request named none.</returns>
    /// <exception cref="MailboxQueryPageSizeOutOfRangeException">Thrown when <paramref name="value" /> is named and outside the accepted range.</exception>
    public static MailboxQueryPageSize FromRequested(int? value) => value is { } requested
        ? Create(requested)
        : Default;

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString(CultureInfo.InvariantCulture);
}
