// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { DeclaredSource } from '@mailfathom/client-backend';
import { useLocalization, type Translate } from '../../localization/useLocalization';
import { useAnswerSources } from '../answerSources';

// One source a block rests on, drawn where the block rests on it. The design project states the rule this implements
// in its own legend: *a citation is always an activatable element with its own name — never a bare superscript*, which
// is why this is a button carrying the source's own label rather than a number a reader has to match against a list.
//
// The number on it is the place the source holds among the ones this block names, and it is the number alone that is
// drawn: the label is long — a file name, a subject — and a chip carrying one would push the prose it sits in apart.
// What carries the label is the accessible name, which is what a reader following the citation is actually given.
//
// **A source the run never declared is still drawn.** A citation names a source declared before it, so a name this
// client holds nothing for is one it was not given — which is a thing to say rather than a chip to leave out, because
// a fact silently shorn of its citation reads as a fact nobody cited.

/**
 * What the citation is called, and — where it cannot be pressed — what it says instead.
 *
 * Three different reasons a citation cannot be followed, and each says its own sentence rather than one wrapping
 * another: a source this client was never given is already an explanation, a target this client cannot open says which
 * half is missing, and saying *not built yet* over either would report the whole capability as missing on a screen
 * where every other citation follows perfectly well.
 */
function wordCitation(
    declared: DeclaredSource | undefined,
    at: string,
    followable: boolean,
    translate: Translate,
): string {
    if (declared === undefined) {
        return translate('answer.citationUndeclared', { position: at });
    }

    const named = translate('answer.citation', { position: at, source: declared.label });

    if (declared.target === null) {
        return translate('answer.citationUnfollowable', { position: at, source: declared.label });
    }

    return followable ? named : translate('control.notBuiltYet', { control: named });
}

/**
 * One citation, as a chip that follows the source it names.
 *
 * @param source The name the run's blocks refer to the source by.
 * @param position Where the source stands among the ones this block names, counted from one.
 */
export function Citation({ source, position }: { readonly source: string; readonly position: number }) {
    const { locale, translate } = useLocalization();
    const { sources, follow } = useAnswerSources();

    const declared = sources.get(source);
    const at = new Intl.NumberFormat(locale).format(position);

    // Nowhere to follow it to is not a reason to draw it as though there were: the control stays reachable and says
    // why it cannot act, which is what `controls/PlannedControl.tsx` does for every other control in this position.
    //
    // A target this client cannot open is the reason the run itself decides: the plan declared a kind written after
    // this build was, so the citation is named and numbered and there is nowhere to press it to.
    const following =
        follow !== null && declared !== undefined && declared.target !== null
            ? () => {
                  follow(source);
              }
            : undefined;

    const unfollowable = following === undefined;
    const why = wordCitation(declared, at, !unfollowable, translate);

    return (
        <button
            aria-disabled={unfollowable ? true : undefined}
            aria-label={why}
            className={`rounded px-1.75 py-0.5 align-baseline text-2xs tabular-nums transition ${
                unfollowable
                    ? 'cursor-not-allowed bg-rail text-muted'
                    : 'bg-accent-soft text-accent-deep hover:bg-accent hover:text-on-accent'
            }`}
            title={why}
            type="button"
            onClick={following}
        >
            {at}
        </button>
    );
}
