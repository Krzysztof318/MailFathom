// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useCallback, useLayoutEffect, useRef, useState } from 'react';

// How a strip of controls fits the width it was given, which the design project decides by measuring rather than by a
// breakpoint: the controls keep their names in words for as long as the words fit, and only then give something up.
// What they give up first is the control that writes a message — it leaves the strip for the floating control over
// the list, which is the phone's form of it — and only if the names still do not fit do they all become symbols. A
// strip that switched at a width would drop nine names at once on a window three pixels too narrow for one of them.
//
// The measurement is the sum of what the controls need in their widest form, cached from the last render that drew
// that form. A strip already drawn as symbols has no words to measure, so what it compares against the room it has is
// what the words needed the last time they were drawn — which is why the two sums are kept rather than remeasured.
// A margin of `auto` is not counted: it is what spreads a strip across its width, not what a control needs.

/**
 * `labelled` draws every control with its name and the primary control in the strip; `floating` keeps the names and
 * moves the primary control over the list; `symbols` draws the symbols alone, with the primary control over the list.
 */
export type StripFit = 'labelled' | 'floating' | 'symbols';

export interface StripFitting {
    /** Attached to the strip element itself, whose direct children are what is measured. */
    readonly strip: (element: HTMLElement | null) => void;

    /** How the strip fits at the width it has. */
    readonly fit: StripFit;
}

// The room a strip may exceed by and still be read as fitting, so a sum a fraction of a pixel over is not a reflow.
const tolerance = 1;

// How far a control's own margin counts towards what it needs; `auto` reads as none and a larger one is a spring.
const marginCounted = 8;

function marginOf(declared: string): number {
    const parsed = Number.parseFloat(declared);

    return Number.isNaN(parsed) ? 0 : Math.min(parsed, marginCounted);
}

/**
 * How a strip fits the width it has, for a strip whose first two children are the primary control and the divider
 * after it when `withPrimary` holds. `contents` names what the strip holds — the language, whether writing is offered —
 * and a change to it forgets the sums, because they were measured over controls that are no longer there.
 */
export function useStripFit(withPrimary: boolean, contents: string): StripFitting {
    const [fit, setFit] = useState<StripFit>('labelled');
    const element = useRef<HTMLElement | null>(null);
    const needed = useRef<{ contents: string; full: number | null; floating: number | null }>({
        contents,
        full: null,
        floating: null,
    });

    // Measured against the form the strip was drawn in, which is the `fit` this closure was made for: the sums are
    // read from the controls on the screen, and what they say depends on which controls those are. The decision is
    // taken here rather than inside the state update, because a strip is measured twice in one commit — once as the
    // element attaches and once from the layout effect — and an update reading the screen when it runs would read the
    // second time from a strip already drawn for the first answer.
    const measure = useCallback((): void => {
        const strip = element.current;

        if (strip === null) {
            return;
        }

        if (needed.current.contents !== contents) {
            needed.current = { contents, full: null, floating: null };
        }

        const room = strip.clientWidth;
        const children = Array.from(strip.children).filter(
            (child): child is HTMLElement => child instanceof HTMLElement,
        );

        if (room === 0 || children.length === 0) {
            return;
        }

        const sums = needed.current;

        if (fit !== 'symbols') {
            const style = getComputedStyle(strip);
            const gap = Number.parseFloat(style.columnGap) || 0;
            let sum = (Number.parseFloat(style.paddingLeft) || 0) + (Number.parseFloat(style.paddingRight) || 0);

            sum += gap * (children.length - 1);

            for (const child of children) {
                const own = getComputedStyle(child);

                sum += child.offsetWidth + marginOf(own.marginLeft) + marginOf(own.marginRight);
            }

            const primary = children[0];
            const divider = children[1];

            if (
                fit === 'labelled' &&
                withPrimary &&
                children.length > 2 &&
                primary !== undefined &&
                divider !== undefined
            ) {
                sums.full = sum;
                sums.floating = sum - primary.offsetWidth - divider.offsetWidth - gap * 2;
            } else {
                sums.floating = sum;
                sums.full ??= sum;
            }
        }

        // Nothing to compare against — the sums were forgotten while the strip stood as symbols, which measure
        // nothing — so the names are drawn once more to be measured, rather than the strip staying as symbols for
        // as long as the window lives.
        if (sums.floating === null) {
            setFit('labelled');

            return;
        }

        if (withPrimary && sums.full !== null && sums.full <= room + tolerance) {
            setFit('labelled');
        } else if (sums.floating <= room + tolerance) {
            setFit(withPrimary ? 'floating' : 'labelled');
        } else {
            setFit('symbols');
        }
    }, [withPrimary, contents, fit]);

    // The newest measurement, for the two callers below that are set up once and outlive any one render: the
    // observer and the typeface's arrival. Each is armed once rather than re-armed on every change of fit, because
    // re-arming the second is what looped — the arrival forgot the sums and remeasured, the remeasure changed the fit,
    // the change re-armed the arrival, and the strip flipped between two forms in a chain of microtasks that never
    // let a frame through.
    const latest = useRef(measure);
    const watching = useRef<ResizeObserver | null>(null);

    // After every render, because the sums come from what was just drawn.
    useLayoutEffect(() => {
        latest.current = measure;
        measure();
    });

    const strip = useCallback((attached: HTMLElement | null): void => {
        watching.current?.disconnect();
        watching.current = null;
        element.current = attached;

        if (attached !== null && typeof ResizeObserver === 'function') {
            watching.current = new ResizeObserver(() => {
                latest.current();
            });
            watching.current.observe(attached);
        }

        latest.current();
    }, []);

    // Once, when the typeface arrives: what the same controls need changes without anything React drew changing, so
    // the sums are forgotten and read again from whichever form is on the screen.
    useLayoutEffect(() => {
        if (typeof document.fonts === 'undefined') {
            return;
        }

        let listening = true;

        void document.fonts.ready.then(() => {
            if (listening) {
                needed.current = { ...needed.current, full: null, floating: null };
                latest.current();
            }
        });

        return () => {
            listening = false;
        };
    }, []);

    return { strip, fit };
}
