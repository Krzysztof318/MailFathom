// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef } from 'react';
import { Icon } from '../controls/Icon';
import { SurfaceControl } from '../controls/SurfaceControl';
import { useLocalization } from '../localization/useLocalization';

// The control the design project puts on a message's head, and the question it asks before anything is shown. Pressing
// it opens neither a frame nor a read: it opens a confirmation, because what the surface behind it draws is markup a
// stranger wrote and the reader is owed the chance to stay where they are.
//
// The question names what that markup can carry and what this client does about it, and it is careful about which half
// is which. Scripts and remote resources are blocked; the sender may still be able to tell the message was opened,
// because a reader who then asks for the pictures has asked for exactly that. Overstating either half is the failure
// mode a confirmation has — a reader who learns the words are reassurance stops reading them.
//
// Nothing about either answer is written down, per message or otherwise. Declining leaves the message as it was and
// pressing again asks again; accepting opens the surface for as long as it stays open and no longer, which is what
// `workspace/rememberedWorkspace.ts` keeps true across a reload.
//
// The platform's own modal dialog, so four things are the browser's rather than this file's: the page behind it is
// inert, focus moves into it and is held there, Escape leaves it, and leaving it puts focus back on the control that
// opened it. Both answers therefore leave through `close()`, and which one it was travels the way the platform carries
// it — in the return value.

const markupShows = 'show-the-markup';

export function ShowFullHtml({ onShow }: { readonly onShow: () => void }) {
    const { translate } = useLocalization();
    const asked = useRef<HTMLDialogElement>(null);

    return (
        <>
            <SurfaceControl
                label={translate('fullHtml.show')}
                icon="code"
                onActivate={() => {
                    asked.current?.showModal();
                }}
            />

            <dialog
                ref={asked}
                aria-labelledby="show-full-html"
                className="m-auto w-104 max-w-full rounded-3xl border border-line bg-panel p-5 text-text shadow-dialog backdrop:bg-scrim"
                onClose={() => {
                    if (asked.current?.returnValue === markupShows) {
                        onShow();
                    }
                }}
            >
                <div className="flex flex-col gap-3.5">
                    <div className="flex items-start gap-2.5">
                        <Icon name="warning" className="mt-0.5 size-5 shrink-0 text-warning" />

                        <div className="flex min-w-0 flex-col gap-1.5">
                            <h2 id="show-full-html" className="text-xl font-semibold">
                                {translate('fullHtml.question')}
                            </h2>

                            <p className="text-base text-muted">{translate('fullHtml.whatItCanCarry')}</p>
                            <p className="text-base text-muted">{translate('fullHtml.whatIsBlocked')}</p>
                        </div>
                    </div>

                    <div className="flex flex-wrap justify-end gap-2">
                        <button
                            type="button"
                            className="rounded-lg border border-line bg-sunken px-3.75 py-2 text-base text-text-soft transition hover:bg-hover"
                            onClick={() => {
                                asked.current?.close();
                            }}
                        >
                            {translate('fullHtml.stayReduced')}
                        </button>

                        <button
                            type="button"
                            className="rounded-lg bg-accent px-4 py-2 text-base font-semibold text-on-accent transition hover:opacity-90"
                            onClick={() => {
                                asked.current?.close(markupShows);
                            }}
                        >
                            {translate('fullHtml.confirm')}
                        </button>
                    </div>
                </div>
            </dialog>
        </>
    );
}
