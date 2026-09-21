// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import type {
    CitedFragment,
    CitedMessage,
    ClientFailureReason,
    ClientSession,
    DeclaredSource,
    MailAttachment,
    MailFathomTransport,
    ResolvedCitation,
} from '@mailfathom/client-backend';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { wordInstant } from '../localization/instants';
import { sizeOf } from '../localization/octets';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { useScreenLayer } from '../shell/screenLayers';
import { useDesktopComposition } from '../shell/useWideWorkspace';
import { sourceKinds } from './blocks/blockWording';
import { useCitedEvidence, type CitedEvidence } from './useCitedEvidence';

// Where a fact is checked against the correspondence it was drawn from. Every citation in every block leads here, so
// the promise the product rests on — each fact leads to its source — is kept in one place rather than in nine.
//
// **Checking a fact never costs the answer.** On a wide window the source stands beside the result and the result is
// never taken off the screen; on a narrow one it covers the screen and the result is behind it, untouched and at the
// position it was left at. Leaving either returns the reader to the citation they pressed.
//
// **The three outcomes are three states and none of them is an error.** A source resolves to what it holds; a source
// whose passage has been re-cut since the answer was composed keeps the message it came from and says the passage is
// gone; and a source this sign-in may not read says so and keeps the fact it backs. Only a read that did not happen at
// all is drawn as a failure, and each way it can fail says what to do about it.
//
// **The whole message is one further action**, and it is a landing rather than a link: the surface drawing the answer
// is told which message to open, and the reader arrives in the Mail space at that message inside its conversation.

/** How each way of failing to read a source is said, which is a different sentence from failing to read a run. */
const readFailures: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'evidence.unauthenticated',
    unauthorized: 'evidence.unauthorized',
    unavailable: 'evidence.unavailable',
    unreadable: 'evidence.unreadable',
    missing: 'evidence.missing',
};

/**
 * The source somebody is checking, beside the answer on a wide window and over it on a narrow one.
 *
 * @param source The source being checked, or `null` where nothing is — which is this panel's resting state.
 * @param session Who is asking, or `null` where nobody is signed in.
 * @param transport How the read of the source goes out.
 * @param onDismiss What leaving the source does, which is the surface holding the selection letting go of it.
 * @param onOpenMessage What reaching the whole message does, and nothing where this surface offers nowhere to go.
 */
export function EvidenceInspector({
    source,
    session,
    transport,
    onDismiss,
    onOpenMessage,
}: {
    readonly source: DeclaredSource | null;
    readonly session: ClientSession | null;
    readonly transport: MailFathomTransport;
    readonly onDismiss: () => void;
    readonly onOpenMessage?: ((storedEmailId: string) => void) | undefined;
}) {
    const { translate } = useLocalization();
    const desktop = useDesktopComposition();
    const evidence = useCitedEvidence(session, transport, source?.target ?? null);

    const panel = useRef<HTMLDivElement>(null);
    const openedFrom = useRef<HTMLElement | null>(null);

    // The one thing on this surface that is not a render: where the keyboard is. A source arrives because somebody
    // pressed a citation somewhere else on the screen, so the panel takes focus as the source opens and the citation
    // gets it back as it goes — which is what makes checking a fact a detour rather than a place to be stranded in.
    // The modal composition holds focus itself and restores it on the way out; doing it here as well costs one `focus`
    // on an element that already has it, and it is what the panel composition has no platform behaviour to lean on.
    useEffect(() => {
        if (source === null) {
            const opener = openedFrom.current;

            openedFrom.current = null;
            opener?.focus();

            return;
        }

        const active = document.activeElement;
        const pressed =
            active instanceof HTMLElement && active !== document.body && panel.current?.contains(active) !== true;

        if (pressed) {
            openedFrom.current = active;
            panel.current?.focus();
        }
    }, [source]);

    const named = translate('evidence.panel');

    const held = (
        <div aria-label={named} className="flex min-h-0 flex-col gap-3.5 p-4" ref={panel} role="region" tabIndex={-1}>
            {source === null ? <RestingPanel /> : null}

            {source === null ? null : (
                <>
                    <header className="flex flex-col gap-1.5">
                        <Control
                            className="self-start"
                            icon="arrow_back"
                            label={translate('evidence.dismiss')}
                            shape="labelled"
                            onPress={onDismiss}
                        />

                        <div className="flex flex-wrap items-center gap-2">
                            <h2 className="text-md font-semibold">{source.label}</h2>

                            {source.target === null ? null : (
                                <span className="flex items-center gap-1 rounded-full bg-rail px-2.25 py-0.5 text-2xs text-muted">
                                    <Icon className="size-3.25" name={sourceKinds[source.target.kind].icon} />
                                    {translate(sourceKinds[source.target.kind].label)}
                                </span>
                            )}
                        </div>
                    </header>

                    {source.target === null ? (
                        <p className="text-sm text-muted text-pretty">{translate('evidence.unfollowable')}</p>
                    ) : (
                        <FollowedSource evidence={evidence} medium={source.medium} onOpenMessage={onOpenMessage} />
                    )}
                </>
            )}
        </div>
    );

    // Beside the answer where there is room for it and over the answer where there is not. Which of the two is in the
    // document is the question § *The two heads* says is asked rather than composed against, because a modal and a
    // panel are different elements rather than one element laid out two ways: focus, the back gesture, and whether
    // the result behind it can be reached at all are decided by which of them it is.
    if (desktop || source === null) {
        return <div className="rounded-lg border border-line bg-rail desktop:sticky desktop:top-4">{held}</div>;
    }

    return (
        <EvidenceWindow label={named} onClosed={onDismiss}>
            {held}
        </EvidenceWindow>
    );
}

/** What the panel says while nothing is being checked, which is what it is for rather than a blank column. */
function RestingPanel() {
    const { translate } = useLocalization();

    return (
        <div className="flex flex-col gap-1.5">
            <h2 className="text-2xs tracking-widest text-muted uppercase">{translate('evidence.panel')}</h2>
            <p className="text-sm text-muted text-pretty">{translate('evidence.resting')}</p>
        </div>
    );
}

/**
 * What became of following one source, which is a state in every case and a failure in only one of them.
 *
 * A read that did not happen is drawn where the words would have been rather than as a notice over them: what the
 * reader asked for is the source, and saying the deployment did not answer in the place the paragraph would have
 * stood is an answer to that question.
 */
function FollowedSource({
    evidence,
    medium,
    onOpenMessage,
}: {
    readonly evidence: CitedEvidence | null;
    readonly medium: DeclaredSource['medium'];
    readonly onOpenMessage: ((storedEmailId: string) => void) | undefined;
}) {
    const { translate } = useLocalization();

    if (evidence === null || evidence.state === 'following') {
        return (
            <p className="text-sm text-muted" role="status">
                {translate('evidence.following')}
            </p>
        );
    }

    if (evidence.state === 'failed') {
        return (
            <p className="flex items-start gap-1.75 rounded-md bg-warning-soft px-2.75 py-2 text-sm text-warning-text">
                <Icon className="mt-0.25 size-3.75" name="error" />
                {translate(readFailures[evidence.reason])}
            </p>
        );
    }

    return <CitedSource citation={evidence.citation} medium={medium} onOpenMessage={onOpenMessage} />;
}

/** One source as the deployment answered for it: where it stands, what it holds, and the way to the whole of it. */
function CitedSource({
    citation,
    medium,
    onOpenMessage,
}: {
    readonly citation: ResolvedCitation;
    readonly medium: DeclaredSource['medium'];
    readonly onOpenMessage: ((storedEmailId: string) => void) | undefined;
}) {
    const { translate } = useLocalization();
    const { message } = citation;

    if (citation.outcome === 'PrivateSource') {
        return (
            <div className="flex flex-col gap-2">
                <span className="flex w-fit items-center gap-1 rounded-full bg-rail px-2.25 py-0.5 text-2xs text-muted">
                    <Icon className="size-3.25" name="lock" />
                    {translate('answer.sourcePrivate')}
                </span>

                <p className="text-sm text-faint italic text-pretty">{translate('answer.sourcePrivateBody')}</p>
                <p className="text-sm text-muted text-pretty">{translate('evidence.privateRemedy')}</p>
            </div>
        );
    }

    // The one resolution carrying no message at all: this deployment holds the message and could not read its own
    // stored copy of it. It is not a private source and it is not a passage that moved, so it says its own sentence.
    if (message === null) {
        return <p className="text-sm text-muted text-pretty">{translate('evidence.unreadableCopy')}</p>;
    }

    const nowhereInside = citation.fragment === null && citation.file === null;

    return (
        <div className="flex min-h-0 flex-col gap-3">
            <CitedMessageLine message={message} />

            {citation.outcome === 'Unresolvable' ? (
                <p className="flex items-start gap-1.75 rounded-md bg-warning-soft px-2.75 py-2 text-sm text-warning-text">
                    <Icon className="mt-0.25 size-3.75" name="history" />
                    {translate('evidence.unresolvable')}
                </p>
            ) : null}

            {citation.fragment === null ? null : <CitedWords fragment={citation.fragment} medium={medium} />}
            {citation.file === null ? null : <CitedFile file={citation.file} />}

            {citation.outcome === 'Resolved' && nowhereInside ? (
                <p className="text-sm text-muted text-pretty">{translate('evidence.wholeMessage')}</p>
            ) : null}

            {onOpenMessage === undefined ? null : (
                <Control
                    className="self-start"
                    icon="forward"
                    label={translate('evidence.openInMail')}
                    shape="primary"
                    onPress={() => {
                        onOpenMessage(message.storedEmailId);
                    }}
                />
            )}
        </div>
    );
}

/** Where the source stands: the message it is part of, where it was read from, and when it arrived. */
function CitedMessageLine({ message }: { readonly message: CitedMessage }) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();

    const at = wordInstant(message.receivedAt ?? message.sentAt, locale, 'full', timeZone);

    return (
        <div className="flex flex-col gap-0.5">
            <p className="text-sm font-medium">{message.subject ?? translate('message.noSubject')}</p>

            <p className="text-xs text-faint">
                {translate('evidence.standingIn', { account: message.account, folder: message.folder })}
            </p>

            {at === null ? null : <p className="text-xs text-faint">{at}</p>}
        </div>
    );
}

/**
 * The words the fact was drawn from, quoted where they were written and stated plainly where they were not.
 *
 * The same rule the evidence list holds to, for the reason ADR 0030 gives: this deployment's own account of a picture
 * presented as a quotation is a guess promoted to evidence, which is exactly what somebody checking a fact would be
 * misled by.
 */
function CitedWords({
    fragment,
    medium,
}: {
    readonly fragment: CitedFragment;
    readonly medium: DeclaredSource['medium'];
}) {
    const words = 'rounded-r-md border-l-3 border-highlight-line bg-highlight px-3.5 py-3 text-sm text-pretty';

    return medium === 'Depicted' ? (
        <p className={words}>{fragment.text}</p>
    ) : (
        <q className={`block ${words}`}>{fragment.text}</q>
    );
}

/** The file the fact was drawn from, named and measured rather than fetched. */
function CitedFile({ file }: { readonly file: MailAttachment }) {
    const { locale, translate } = useLocalization();

    return (
        <p className="flex flex-wrap items-center gap-2 rounded-md border border-line bg-sunken px-3 py-2.25 text-sm">
            <span className="min-w-0 truncate text-text">{file.fileName ?? translate('attachment.unnamed')}</span>
            <span className="text-faint">{sizeOf(file.sizeOctets, locale)}</span>
        </p>
    );
}

/**
 * The narrow composition, which is a modal over the result rather than a panel beside it.
 *
 * It is the platform's own `dialog` for the reason `mailSpace/SurfaceWindow.tsx` gives about the same choice: focus
 * moves in and is held there, Escape leaves it, the result behind it is out of reach while it stands, and closing
 * hands focus back — four obligations the element already meets and a hand-written trap would meet worse.
 */
function EvidenceWindow({
    label,
    onClosed,
    children,
}: {
    readonly label: string;
    readonly onClosed: () => void;
    readonly children: ReactNode;
}) {
    // Held as state rather than in a ref because opening the window is an effect that runs when the element arrives,
    // and a ref is a value a render may not read.
    const [standing, setStanding] = useState<HTMLDialogElement | null>(null);

    const close = useCallback(() => {
        standing?.close();
    }, [standing]);

    useEffect(() => {
        standing?.showModal();
    }, [standing]);

    // It stands over the result, so the back gesture leaves the source before it navigates anywhere, and moving to
    // another destination leaves it behind rather than over the screen somebody arrives at.
    useScreenLayer(true, close);

    return (
        <dialog
            aria-label={label}
            className="m-0 h-full max-h-none w-full max-w-none bg-panel text-text backdrop:bg-scrim"
            ref={setStanding}
            onClose={onClosed}
        >
            {children}
        </dialog>
    );
}
