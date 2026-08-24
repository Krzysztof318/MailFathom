// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.UI.Xaml.Data;

namespace MailFathom.Client.Presentation;

/// <summary>Puts a control on the screen only where the model outright affirmed the condition behind it.</summary>
/// <remarks>
/// <para>
/// A converter rather than a <see cref="Visibility"/> on a model, because a model that named a WinUI type would have
/// taken on a view's work — the rule <c>frontend/src/AGENTS.md</c> § <em>MVUX is the state model</em> states. What the
/// model exposes is whether a capability is offered or withheld; turning either into a composition is the view's
/// decision, and this is where it is taken.
/// </para>
/// <para>
/// Anything that is not <see langword="true" /> collapses, which is what makes the unknown case safe and is why both
/// sides of a decision are stated as their own affirmative rather than as one flag read forwards and backwards. A
/// property carrying no answer yet — the session is still being fetched, or the fetch failed — reaches a binding as
/// the type's default, so a control shown on a negative would be on the screen before anything had decided anything.
/// Offering a space that way would put it in front of somebody who may not have it; saying a space is withheld that
/// way would report a fetch still under way as a capability taken away.
/// </para>
/// </remarks>
internal sealed class AffirmedVisibilityConverter : IValueConverter
{
    /// <summary>Converts an affirmed flag into the visibility a control takes.</summary>
    /// <param name="value">What the model said, which is <see langword="true" />, <see langword="false" />, or nothing yet.</param>
    /// <param name="targetType">The property's type, which this does not vary by.</param>
    /// <param name="parameter">Unused: what is affirmed is the value rather than something the view states.</param>
    /// <param name="language">Unused: a visibility is not a word.</param>
    /// <returns><see cref="Visibility.Visible"/> only where the value is <see langword="true" />.</returns>
    public object Convert(object? value, Type targetType, object? parameter, string? language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Not supported: what the client offers is decided by the session rather than by a control's visibility.</summary>
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
