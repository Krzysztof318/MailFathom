// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, type RefObject } from 'react';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { whatWouldBeMissing, type Composition, type SendCaution } from './composition';

// Sending, and the question in front of it. Nothing is sent without this: the commonest irreversible mistake in mail is
// not the words, it is who received them, so the confirmation is where every address is read one last time — the blind
// copies included, those being the ones a header row never shows back.
//
// The control and the question belong in one component for the reason `TabStrip.tsx` gives about the same pairing: the
// dialog is the platform's own, so whether it is open is the element's state rather than a second copy of it, and
// leaving it either way puts focus back on the control that opened it without anything here remembering which.
const messageGoes = 'send';

// What a send would go out without, each said as a sentence rather than as a field name. Exhaustive by its own type, so
// a caution added to the composition fails to compile until it has words.
const cautionSaid: Readonly<Record<SendCaution, MessageKey>> = {
    noRecipient: 'compose.cautionNoRecipient',
    noSubject: 'compose.cautionNoSubject',
    noWords: 'compose.cautionNoWords',
};

export function SendConfirmation({
    asked,
    composition,
    disabled,
    onSend,
}: {
    /**
     * The dialog itself, held by the composer so that the keyboard shortcut it draws reaches the same question.
     *
     * The element is the state, which is what the note above is about: two ways to ask are still one dialog, and
     * neither this component nor the composer holds a second copy of whether it is open.
     */
    readonly asked: RefObject<HTMLDialogElement | null>;

    readonly composition: Composition;

    /** Whether the message is already on its way, which is what keeps one press from queueing two. */
    readonly disabled: boolean;

    readonly onSend: () => void;
}) {
    const { locale, translate } = useLocalization();
    const question = useId();
    const missing = whatWouldBeMissing(composition);
    const addresses = new Intl.ListFormat(locale, { style: 'long', type: 'conjunction' });

    // Each header is said only where somebody is written in it, because a confirmation that lists two empty headers is
    // one a reader learns to skim — and skimming is exactly what this exists to stop.
    function headers(): readonly { readonly key: MessageKey; readonly written: readonly string[] }[] {
        return [
            { key: 'compose.confirmTo' as const, written: composition.to },
            { key: 'compose.confirmCc' as const, written: composition.cc },
            { key: 'compose.confirmBcc' as const, written: composition.bcc },
        ].filter((header) => header.written.length > 0);
    }

    return (
        <>
            <button
                type="button"
                disabled={disabled}
                className="flex items-center gap-1.75 rounded-lg bg-accent px-4 py-2 text-sm font-semibold text-on-accent transition hover:opacity-90 disabled:opacity-60"
                onClick={() => {
                    asked.current?.showModal();
                }}
            >
                <Icon name="send" className="size-4.5" />
                {translate('compose.send')}
            </button>

            <dialog
                ref={asked}
                aria-labelledby={question}
                className="m-auto w-100 max-w-full rounded-3xl border border-line bg-panel p-5 text-text shadow-dialog backdrop:bg-scrim"
                onClose={() => {
                    if (asked.current?.returnValue === messageGoes) {
                        onSend();
                    }
                }}
            >
                <div className="flex flex-col gap-3.5">
                    <h2 id={question} className="text-xl font-semibold">
                        {translate('compose.confirmQuestion')}
                    </h2>

                    <div className="flex flex-col gap-1">
                        {headers().length === 0 ? (
                            <p className="text-base text-muted">{translate('compose.confirmNobody')}</p>
                        ) : (
                            headers().map((header) => (
                                <p key={header.key} className="text-base text-muted text-pretty">
                                    {translate(header.key, { addresses: addresses.format(header.written) })}
                                </p>
                            ))
                        )}

                        <p className="text-base text-muted text-pretty">
                            {translate('compose.confirmSubject', {
                                subject:
                                    composition.subject.trim() === ''
                                        ? translate('compose.confirmNoSubject')
                                        : composition.subject,
                            })}
                        </p>
                    </div>

                    {missing.length === 0 ? null : (
                        <ul className="flex flex-col gap-1.75 rounded-2xl border border-warning bg-warning-soft px-3.25 py-2.75">
                            {missing.map((caution) => (
                                <li key={caution} className="flex items-start gap-2 text-base text-warning-text">
                                    <Icon name="warning" className="mt-0.5 size-4" />
                                    {translate(cautionSaid[caution])}
                                </li>
                            ))}
                        </ul>
                    )}

                    <div className="flex justify-end gap-2">
                        <button
                            type="button"
                            className="rounded-lg border border-line bg-sunken px-3.75 py-2 text-base text-text-soft transition hover:bg-hover"
                            onClick={() => {
                                asked.current?.close();
                            }}
                        >
                            {translate('compose.backToEditing')}
                        </button>

                        <button
                            type="button"
                            className="rounded-lg bg-accent px-4 py-2 text-base font-semibold text-on-accent transition hover:opacity-90"
                            onClick={() => {
                                asked.current?.close(messageGoes);
                            }}
                        >
                            {translate(missing.length === 0 ? 'compose.send' : 'compose.sendAnyway')}
                        </button>
                    </div>
                </div>
            </dialog>
        </>
    );
}
