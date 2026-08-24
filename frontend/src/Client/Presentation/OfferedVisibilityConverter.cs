// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.UI.Xaml.Data;

namespace MailFathom.Client.Presentation;

/// <summary>Puts a control on the screen only where the model said the capability behind it is offered.</summary>
/// <remarks>
/// <para>
/// A converter rather than a <see cref="Visibility"/> on a model, because a model that named a WinUI type would have
/// taken on a view's work — the rule <c>frontend/src/AGENTS.md</c> § <em>MVUX is the state model</em> states. What the
/// model exposes is whether a capability is offered; turning that into a composition is the view's decision, and this
/// is where it is taken.
/// </para>
/// <para>
/// Anything that is not <see langword="true" /> collapses, which is what makes the unknown case safe. A feed carrying
/// no value yet — the session is still being fetched, or the fetch failed — reaches a binding as nothing at all, and
/// reading that as "offer it" would put a space in front of somebody before anything had said they may use it.
/// </para>
/// </remarks>
internal sealed class OfferedVisibilityConverter : IValueConverter
{
    /// <summary>Gets or sets whether this shows what is withheld rather than what is offered.</summary>
    /// <remarks>
    /// Two instances of one rule rather than a second converter, because the two are the same decision read from the
    /// two sides: the control that offers a capability and the control that explains its absence are never both on the
    /// screen, and neither of them is there while nothing has said which it should be.
    /// </remarks>
    public bool Withheld { get; set; }

    /// <summary>Converts an offered flag into the visibility a control takes.</summary>
    /// <param name="value">What the model said, which is <see langword="true" />, <see langword="false" />, or nothing yet.</param>
    /// <param name="targetType">The property's type, which this does not vary by.</param>
    /// <param name="parameter">Unused: which side of the rule this reads is stated on the instance rather than per binding.</param>
    /// <param name="language">Unused: a visibility is not a word.</param>
    /// <returns><see cref="Visibility.Visible"/> only where the value is the outright answer this instance reads.</returns>
    public object Convert(object? value, Type targetType, object? parameter, string? language) =>
        Equals(value, !this.Withheld) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Not supported: what is offered is decided by the session rather than by a control's visibility.</summary>
    /// <param name="value">Unused.</param>
    /// <param name="targetType">Unused.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="language">Unused.</param>
    /// <returns>Nothing; this always throws.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, string? language) =>
        throw new NotSupportedException(
            "What the client offers is read from the session, so a control's visibility is never written back to it.");
}
