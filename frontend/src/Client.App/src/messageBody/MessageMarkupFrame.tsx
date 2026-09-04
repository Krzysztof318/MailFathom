// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';

// The one file in this client that writes an `iframe`'s `srcDoc`, and the only place a message's own markup is drawn
// as markup. Everywhere else under `src/` the lint rule refuses it outright, and the exception is written into
// `eslint.config.ts` against this path rather than waived at the call site — so a second frame is a configuration diff
// a reviewer meets rather than a line nobody sees. ADR 0024 names this file as that exception, and names one file for
// both markup surfaces rather than one per surface, which is why the two below stand here together.
//
// **Two mechanisms hold two different promises here, and neither substitutes for the other.** A frame is what stops the
// markup running: `sandbox` with neither `allow-scripts` nor `allow-same-origin` denies script, forms, popups,
// navigation, downloads, and an origin of its own. It is *not* what stops the markup reporting — no sandboxing flag
// the HTML Standard defines governs what a framed document may fetch, so a sandboxed frame would load a tracking pixel
// exactly as an unsandboxed one does. What keeps that out is the representation: the service prepares the markup with
// every remote address already removed, unless the reader asked for this one message's pictures. Weakening either half
// is a change to both.
//
// **The two surfaces carry two different `sandbox` values, and that is ADR 0024's third question rather than drift.**
// The dialog is opened one message at a time, is given its size by the page, and keeps `sandbox=""`. The embedded view
// draws every open message inline at its full height inside one scrolling conversation, and no attribute, property, or
// shipped platform feature fits a sandboxed frame to its content — so it carries `sandbox="allow-scripts"` and nothing
// else, which leaves the framed document an opaque origin that reaches neither the page's DOM, nor its storage, nor a
// cookie, and lets the client's own measuring script report the height. What then holds *nothing in the message runs*
// on that surface is the representation alone: #1484 serves markup with nothing executable in it, so the only script
// in that frame is the one this file put there. That is a promise moved off the platform and onto a build, and ADR 0024
// records it as a cost rather than as free.
//
// What both are drawn on is `--color-sender-markup`, which is the one token in this client that stays the same in both
// themes, and `styles.css` carries the reason beside the declaration rather than here.
//
// Neither draws anything where there is no markup to draw. A frame with an empty document is a white rectangle that
// says nothing, and what a reader is owed instead is a sentence — which is the surface's to say, because only the
// surface knows why the markup is absent.

export function MessageMarkupFrame({ markup }: { readonly markup: string }) {
    const { translate } = useLocalization();

    if (markup === '') {
        return null;
    }

    return (
        <iframe
            title={translate('fullHtml.frame')}
            sandbox=""
            srcDoc={markup}
            className="min-h-0 w-full flex-1 border-0 bg-sender-markup"
        />
    );
}

// The heights this frame is drawn at, and the two bounds on what it will accept from inside itself. They are numbers
// the bridge computes with rather than utilities a screen composes, which is why they stand here as constants instead
// of in the token layer: two of the three are applied to an element whose height is a measurement most of the time.
// Each is the design project's own, named on #1507's acceptance.
const heightBeforeAReportArrives = 320;
const heightWhenNoneArrives = 1_600;
const windowWhenNoneArrives = 620;
const shortestFrame = 80;
const tallestFrame = 40_000;

// How long a frame is given to report before the conversation settles on a height it can be read at. A message that
// never reports is the one case the embedded view scrolls inside itself, which is why the wait is short: a reader
// looking at a blank 320-pixel rectangle has no way of telling a slow message from a broken one.
const reportWaitedFor = 2_500;

// What the parent stops accepting, and why each bound is here. A frame granting `allow-scripts` can post whatever it
// likes, so a document that grew by a pixel per report would otherwise push its own frame open without end; a report
// that moves the height by less than a few pixels is a rounding difference rather than a fit.
const mostAdjustments = 16;
const settledWithin = 3;

// The client's own script, prepended to the `srcDoc` ahead of the message markup. It measures the document at a
// viewport height of zero — otherwise each fitting would enlarge the content it is measuring and the number would grow
// without end — and observes the body rather than the document element, which would close the same loop. It reports by
// `postMessage` and does nothing else, which is what makes granting the flag bounded.
//
// It also stops the framed document scrolling inside itself, which is the design project's `scrolling="no"` written
// the way the platform still has: that attribute is deprecated and the lint set refuses it, and what replaces it is
// `overflow: hidden` on the framed document. The one case the frame is meant to scroll is the one where no report ever
// arrives, and nothing there ran this script to hide it.
const measuringScript = `<script>(function(){var last=0,sends=0;
function measure(){var de=document.documentElement,b=document.body;if(!de||!b)return 0;
var held=de.style.height;de.style.height="0px";
var h=Math.max(b.scrollHeight,b.offsetHeight,Math.ceil(b.getBoundingClientRect().height));
de.style.height=held;return h}
function send(){if(sends>24)return;var h=measure();
if(h&&Math.abs(h-last)>3){last=h;sends++;try{parent.postMessage({height:h},"*")}catch(e){}}}
function boot(){var de=document.documentElement,b=document.body;
if(de)de.style.overflow="hidden";if(b)b.style.overflow="hidden";
if(window.ResizeObserver&&b){try{new ResizeObserver(function(){send()}).observe(b)}catch(e){}}send()}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",boot);else boot();
window.addEventListener("load",send);setTimeout(send,120);setTimeout(send,600);})()</script>`;

/** How the frame arrived at the height it is drawn at, which is the whole of what the strip beneath it says. */
type Fitting = 'measuring' | 'measured' | 'unreported';

interface Fitted {
    readonly height: number;
    readonly adjustments: number;
    readonly fitting: Fitting;
}

const beforeAnythingReported: Fitted = {
    height: heightBeforeAReportArrives,
    adjustments: 0,
    fitting: 'measuring',
};

// What the strip beneath the frame says in each of the three states. The measured one is the sentence the design
// project draws, and it is the footer ADR 0024 keeps: on this surface both halves of it are the representation's,
// because the frame no longer holds the first.
const fittingNotes: Readonly<Record<Fitting, MessageKey>> = {
    measuring: 'body.markupFitting',
    measured: 'body.markupIsolated',
    unreported: 'body.markupNotMeasured',
};

/**
 * One message's own markup, drawn inline in the conversation at the height the framed document reports.
 *
 * The height arrives by `postMessage` and is matched to this frame by its source rather than by its origin: an opaque
 * origin serializes as the string `"null"`, which every sandboxed frame on the page reports and which is therefore no
 * evidence of anything. What means something is that the report came from the `contentWindow` of the frame this
 * component created.
 *
 * @param markup The self-contained representation the service serves, which carries nothing executable and no remote
 * address. Handing this anything else would put a stranger's script in a frame that is allowed to run one.
 */
export function EmbeddedMessageMarkup({ markup }: { readonly markup: string }) {
    const { translate } = useLocalization();
    const frame = useRef<HTMLIFrameElement>(null);
    const [fitted, setFitted] = useState<Fitted>(beforeAnythingReported);

    // The one thing outside React this surface synchronizes with, and it is two: a report arriving from inside the
    // frame, and the wait running out before one does. Both are registered once, because a frame belongs to the
    // message this component was mounted for and a changed message mounts another.
    useEffect(() => {
        function reported(event: MessageEvent): void {
            if (event.source !== frame.current?.contentWindow) {
                return;
            }

            const height = heightIn(event.data);

            if (height !== null) {
                setFitted(fittedTo(height));
            }
        }

        window.addEventListener('message', reported);

        const waitedOut = window.setTimeout(() => {
            setFitted((current) => (current.fitting === 'measuring' ? whereNothingReported : current));
        }, reportWaitedFor);

        return () => {
            window.removeEventListener('message', reported);
            window.clearTimeout(waitedOut);
        };
    }, []);

    if (markup === '') {
        return null;
    }

    return (
        <div className="flex flex-col overflow-hidden rounded-xl border border-line bg-sender-markup">
            {/* The one case this surface scrolls inside itself is a frame that never reported: the conversation is
                what scrolls otherwise, and a message drawn at a guessed height would be cut off or leave a gap. */}
            <div
                className={fitted.fitting === 'unreported' ? 'overflow-y-auto' : 'overflow-hidden'}
                style={fitted.fitting === 'unreported' ? { height: `${String(windowWhenNoneArrives)}px` } : undefined}
            >
                <iframe
                    ref={frame}
                    title={translate('fullHtml.frame')}
                    sandbox="allow-scripts"
                    srcDoc={documentAround(markup)}
                    style={{ height: `${String(fitted.height)}px` }}
                    className="block w-full border-0 bg-sender-markup"
                />
            </div>

            <p className="flex items-center gap-2 border-t border-line bg-sunken px-3 py-1.5 text-xs text-muted">
                <Icon name="lock" className="size-3.5" />
                {translate(fittingNotes[fitted.fitting])}
            </p>
        </div>
    );
}

// The script goes ahead of the message markup, inside the document's own head where it has one, so that it is parsed
// before anything it will measure. The representation is a whole document rather than a fragment, so the head is
// normally there; a representation without one still gets the script first.
function documentAround(markup: string): string {
    const head = markup.indexOf('<head>');

    return head < 0
        ? measuringScript + markup
        : markup.slice(0, head + '<head>'.length) + measuringScript + markup.slice(head + '<head>'.length);
}

// What a report carries, or nothing where it carries no height this surface can act on. The value crossed a trust
// boundary, so it is read out of an unknown rather than asserted, and it is bounded before it reaches an element.
function heightIn(reported: unknown): number | null {
    if (typeof reported !== 'object' || reported === null || !('height' in reported)) {
        return null;
    }

    const { height } = reported;

    if (typeof height !== 'number' || !Number.isFinite(height)) {
        return null;
    }

    return Math.min(Math.max(Math.round(height) + 2, shortestFrame), tallestFrame);
}

const whereNothingReported: Fitted = {
    height: heightWhenNoneArrives,
    adjustments: 0,
    fitting: 'unreported',
};

// A report is taken while the frame is still being fitted, and refused once it has settled or once it has been
// adjusted more times than any real document needs. The wait having run out is final: a frame drawn in its own window
// that then started reporting would resize under a reader who has begun scrolling it.
function fittedTo(height: number): (current: Fitted) => Fitted {
    return (current) => {
        if (current.fitting === 'unreported') {
            return current;
        }

        const settled =
            current.fitting === 'measured' &&
            (current.adjustments >= mostAdjustments || Math.abs(height - current.height) <= settledWithin);

        return settled ? current : { height, adjustments: current.adjustments + 1, fitting: 'measured' };
    };
}
