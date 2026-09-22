// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useId, useRef, useState } from 'react';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';

// What a new conversation offers to start with. The design project's prototype opens a seeded conversation from each;
// a client has none to open, so each asks its question exactly as typing it would.
const starters: readonly MessageKey[] = ['agent.starter.day', 'agent.starter.slipped', 'agent.starter.cases'];

/**
 * Where the reader tells the agent what to do: the field, Send, and Cancel beside it, which is drawn at all times and
 * can be pressed only while an answer is being composed.
 *
 * Sending while an answer is composed steers it rather than starting another — the instruction joins the run in flight
 * and it takes it from its next turn — so there is one field and one Send for both.
 *
 * @param onSend Sends what was typed, answering whether it went, which is what clears the field.
 * @param notSent Why the last send did not go, already in the reader's language, and `null` where it went.
 * @param focusAsked Changed by the screen when what held focus went away, which puts focus in the field.
 */
export function AgentComposer({
    running,
    offersStarters,
    notSent,
    focusAsked,
    onSend,
    onCancel,
}: {
    readonly running: boolean;
    readonly offersStarters: boolean;
    readonly notSent: string | null;
    readonly focusAsked: number;
    readonly onSend: (text: string) => Promise<boolean>;
    readonly onCancel: () => void;
}) {
    const { translate } = useLocalization();
    const field = useId();
    const [text, setText] = useState('');
    const [sending, setSending] = useState(false);
    const input = useRef<HTMLInputElement>(null);

    useEffect(() => {
        if (focusAsked > 0) {
            input.current?.focus();
        }
    }, [focusAsked]);

    async function send(written: string): Promise<void> {
        const question = written.trim();

        if (question.length === 0 || sending) {
            return;
        }

        setSending(true);
        const sent = await onSend(question);
        setSending(false);

        // Against the field as it stands when the send answers: anything typed while it was going is kept.
        if (sent) {
            setText((now) => (now === written ? '' : now));
        }
    }

    return (
        <div className="flex shrink-0 flex-col gap-2.25 border-t border-line bg-panel px-3.5 py-3 workspace:gap-2.5 workspace:px-6.5 workspace:pt-3.5 workspace:pb-4">
            {offersStarters ? (
                <ul aria-label={translate('agent.starters')} className="flex flex-wrap gap-2">
                    {starters.map((starter) => (
                        <li key={starter}>
                            <button
                                type="button"
                                aria-label={translate('agent.askStarter', { question: translate(starter) })}
                                className="rounded-4xl border border-line-strong bg-panel px-3.25 py-1.75 text-base text-text-soft transition hover:border-accent hover:text-accent-deep"
                                onClick={() => {
                                    void send(translate(starter));
                                }}
                            >
                                {translate(starter)}
                            </button>
                        </li>
                    ))}
                </ul>
            ) : null}

            <form
                className="flex w-full max-w-agent-thread items-center gap-3 rounded-xl border-2 border-accent bg-panel px-3.25 py-2.5"
                onSubmit={(event) => {
                    event.preventDefault();
                    void send(text);
                }}
            >
                <label htmlFor={field} className="sr-only">
                    {translate('agent.composerLabel')}
                </label>

                <input
                    ref={input}
                    id={field}
                    value={text}
                    autoComplete="off"
                    placeholder={translate('agent.composerPlaceholder')}
                    className="min-w-0 flex-1 bg-transparent text-lg text-text outline-none placeholder:text-faint"
                    onChange={(event) => {
                        setText(event.target.value);
                    }}
                />

                <button
                    type="submit"
                    disabled={sending}
                    className="rounded-md bg-accent px-4 py-2 text-base whitespace-nowrap text-on-accent transition hover:opacity-90 disabled:opacity-60"
                >
                    {translate('agent.send')} <span aria-hidden="true">{translate('agent.sendKey')}</span>
                </button>

                <button
                    type="button"
                    aria-disabled={!running}
                    aria-label={translate(running ? 'agent.cancelRunning' : 'agent.cancelIdle')}
                    tabIndex={running ? 0 : -1}
                    className={`rounded-md border px-3.5 py-2 text-base whitespace-nowrap transition ${
                        running
                            ? 'border-error bg-error font-semibold text-on-accent hover:opacity-90'
                            : 'cursor-default border-line text-faint'
                    }`}
                    onClick={() => {
                        if (running) {
                            onCancel();
                        }
                    }}
                >
                    {translate('agent.cancel')}
                </button>
            </form>

            {notSent === null ? null : (
                <p role="alert" className="flex items-start gap-1.75 text-sm text-warning-text text-pretty">
                    <Icon name="error" className="mt-0.25 size-3.75 shrink-0" />
                    {notSent}
                </p>
            )}

            <p className="text-xs text-faint">{translate('agent.footer')}</p>
        </div>
    );
}
