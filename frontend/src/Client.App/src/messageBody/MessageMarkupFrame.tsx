// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState, type RefObject } from 'react';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useLinkOpener } from '../shellOperations/linkOpener';

// The one file in this client that writes an `iframe`'s `srcDoc`, and the only place a message's own markup is drawn
// as markup. Everywhere else under `src/` the lint rule refuses it outright, and the exception is written into
// `eslint.config.ts` against this path rather than waived at the call site — so a second frame is a configuration diff
// a reviewer meets rather than a line nobody sees. ADR 0024 names this file as that exception, and names one file for
// both markup surfaces rather than one per surface, which is why the two below stand here together.
//
// **Two mechanisms hold two different promises here, and neither substitutes for the other.** A frame is what stops the
// markup reaching anything: no `allow-same-origin`, so the document holds an opaque origin and reaches neither the
// page's DOM nor its storage nor a cookie, and no `allow-forms`, `allow-popups`, or `allow-top-navigation`, so it
// submits, opens, and navigates nowhere. It is *not* what stops the markup reporting — no sandboxing flag the HTML
// Standard defines governs what a framed document may fetch, so a sandboxed frame would load a tracking pixel exactly
// as an unsandboxed one does. What keeps that out is the representation: the service prepares the markup with every
// remote address already removed, unless the reader asked for this one message's pictures. And since the flag below is
// granted, the representation is what stops the markup *running* as well. Weakening either half is a change to both.
//
// **Both surfaces carry `sandbox="allow-scripts"` and nothing else, which is ADR 0024's third and fourth questions.**
// That leaves each framed document an opaque origin reaching neither the page's DOM, nor its storage, nor a cookie, and
// it grants no `allow-same-origin`, no `allow-forms`, no `allow-popups`, and no `allow-top-navigation`. What it buys is
// two things a sandboxed frame cannot otherwise do: the embedded view fits itself to its content, and — on both
// surfaces — a link the reader clicks is reported out rather than dying silently, which is what a frame granting no
// popup and no top navigation does to every `href` in it. What then holds *nothing in the message runs* is the
// representation alone: #1484 serves markup with nothing executable in it, so the only script in either frame is the
// one this file put there. That is a promise moved off the platform and onto a build, and ADR 0024 records it as a cost
// rather than as free.
//
// **A followed link leaves through the application's own opener rather than out of the frame.** The framed document
// reports the `href` as the sender wrote it and navigates nothing; the parent decides whether that target is one a
// reader may be handed and then asks for it to be opened — a new browsing context on the web head, the system browser
// on the desktop head. So the two heads answer a link in the sender's markup exactly as they answer one in the reading
// pane, through the one operation `shellOperations/linkOpener.ts` resolves, and no screen here learns which head it is.
//
// What both are drawn on is `--color-sender-markup`, which is the one token in this client that stays the same in both
// themes, and `styles.css` carries the reason beside the declaration rather than here.
//
// Neither draws anything where there is no markup to draw. A frame with an empty document is a white rectangle that
// says nothing, and what a reader is owed instead is a sentence — which is the surface's to say, because only the
// surface knows why the markup is absent.

export function MessageMarkupFrame({ markup }: { readonly markup: string }) {
    const { translate } = useLocalization();
    const frame = useRef<HTMLIFrameElement>(null);
    const refused = useFollowedLinks(frame);

    if (markup === '') {
        return null;
    }

    // The dialog is given its size by the page and scrolls inside itself, so it is handed the link script alone. The
    // measuring script would take that scrolling away with it: hiding the framed document's overflow is what an
    // embedded frame needs and is exactly wrong on the one surface that is meant to scroll.
    return (
        <>
            <iframe
                ref={frame}
                title={translate('fullHtml.frame')}
                sandbox="allow-scripts"
                srcDoc={documentAround(markup, linkScript)}
                className="min-h-0 w-full flex-1 border-0 bg-sender-markup"
            />
            {refused ? <p className="px-3 py-1.5 text-sm text-warning">{translate('link.couldNotOpen')}</p> : null}
        </>
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
// The client's own script for reporting a followed link, prepended to both surfaces' `srcDoc`. It cancels the frame's
// own handling of the click and reports the `href` **as the sender wrote it** — the attribute rather than the resolved
// property, because a relative reference resolves against `about:srcdoc` and would arrive as an address that means
// nothing. Whether the target is one a reader may be handed is the parent's decision and is taken there.
//
// The listener is on the document in the capture phase, so a link wrapped in whatever a template put around it is still
// answered by the first handler to see the event. It reads the tree upwards rather than trusting the event target,
// since a click lands on the text node's element — a `span` inside the anchor, the image inside a banner link.
const linkScript = `<script>(function(){
function anchor(n){while(n&&n.nodeType===1){if(n.nodeName==="A")return n;n=n.parentNode}return null}
document.addEventListener("click",function(e){var a=anchor(e.target);if(!a)return;
var href=a.getAttribute("href");if(!href)return;e.preventDefault();
try{parent.postMessage({link:href},"*")}catch(err){}},true)})()</script>`;

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
    const refused = useFollowedLinks(frame);

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
                    srcDoc={documentAround(markup, linkScript + measuringScript)}
                    style={{ height: `${String(fitted.height)}px` }}
                    className="block w-full border-0 bg-sender-markup"
                />
            </div>

            <p className="flex items-center gap-2 border-t border-line bg-sunken px-3 py-1.5 text-xs text-muted">
                <Icon name="lock" className="size-3.5" />
                {translate(fittingNotes[fitted.fitting])}
                {refused ? <span className="text-warning">{translate('link.couldNotOpen')}</span> : null}
            </p>
        </div>
    );
}

// The script goes ahead of the message markup, inside the document's own head where it has one, so that it is parsed
// before anything it will measure or listen on. The representation is a whole document rather than a fragment, so the
// head is normally there; a representation without one still gets the script first.
function documentAround(markup: string, script: string): string {
    const head = markup.indexOf('<head>');

    return head < 0
        ? script + markup
        : markup.slice(0, head + '<head>'.length) + script + markup.slice(head + '<head>'.length);
}

/** The longest target this hands to the opener, past which a report is a payload rather than a place. */
const longestTarget = 4096;

// The schemes a reader may be handed, which is the set the service already admits on a reduced document's link. It is
// an allow-list for the reason `MailLinkReader` gives about one: what a platform opener will act on is decided by the
// operating system rather than here, so what leaves this application has to be a set somebody chose.
const followableSchemes = ['http:', 'https:', 'mailto:', 'tel:'];

/**
 * Opens the links a framed document reports, for the frame this component created and for no other.
 *
 * The report crossed a trust boundary — it was raised by a script running against a stranger's markup — so what
 * arrives is read out of an `unknown` and held against the schemes above before anything is asked to open it. Matching
 * on the source rather than on the origin is the same rule the height report follows and for the same reason: an
 * opaque origin serializes as the string `"null"`, which every sandboxed frame on the page reports.
 *
 * @returns whether the last link this reader followed could not be opened, which the surface draws.
 */
function useFollowedLinks(frame: RefObject<HTMLIFrameElement | null>): boolean {
    const openLink = useLinkOpener();
    const [refused, setRefused] = useState(false);

    useEffect(() => {
        function reported(event: MessageEvent): void {
            if (event.source !== frame.current?.contentWindow) {
                return;
            }

            const target = followableTargetIn(event.data);

            if (target !== null) {
                setRefused(false);

                // The desktop head's opener can genuinely reject — an unregistered scheme, no handler, an operating
                // system that refused — and a press that then does nothing is the defect this whole surface exists to
                // remove wearing a different face. So the failure is drawn, in the same sentence a link in the reading
                // pane draws it in.
                void openLink(target).catch(() => {
                    setRefused(true);
                });
            }
        }

        window.addEventListener('message', reported);

        return () => {
            window.removeEventListener('message', reported);
        };
    }, [frame, openLink]);

    return refused;
}

/** What a report names, or nothing where it names no target this application may open. */
function followableTargetIn(reported: unknown): string | null {
    if (typeof reported !== 'object' || reported === null || !('link' in reported)) {
        return null;
    }

    const { link } = reported;

    if (typeof link !== 'string' || link.length === 0 || link.length > longestTarget) {
        return null;
    }

    try {
        return followableSchemes.includes(new URL(link).protocol) ? link : null;
    } catch {
        // A relative reference resolves against nothing here, exactly as it does in the framed document, so it names
        // no place a reader could be taken to.
        return null;
    }
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
