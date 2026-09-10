// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { useListedMail } from '../messageList/useListedMail';

// The one control in the navigation that goes nowhere and changes nothing: it asks the client to read again what it is
// already showing. Today that is the mail in front of the reader and the notification centre, and the list is expected
// to grow rather than the control to multiply — a second refresh button for a second list would be a client where
// *refresh* means something different in each corner of it.
//
// **It never empties what it is refreshing.** Each surface reads its own leading end again and keeps every row it has
// while the answer is on the wire, which is the standing rule for every list in this client: a list is drawn as a
// skeleton once, when it has nothing, and every later change reaches the rows it actually touched.
//
// **It says nothing about being busy, because the surfaces do.** The list draws the rows it is reading again and the
// centre says it is reading, each in the place its answer will land — which is where somebody is looking. The design
// project spins this control's own symbol instead, and it is drawn there as a dashed demo affordance whose one press
// invents an arrival, an edit and a removal; a real control saying it is busy in a corner nobody is watching would be
// the wrong half of that.
//
// It is drawn the two ways the navigation around it is, exactly as the bell beside it: an item with its name under it
// where the navigation is a sheet, and the symbol alone in the rail of a wide window.

export function Refresh({ readNotificationsAgain }: { readonly readNotificationsAgain: () => void }) {
    const { translate } = useLocalization();

    // What *everything* means is composed here rather than handed in whole, because the two halves are reached
    // differently and neither is this control's: the mail is whatever list is on the screen, which only that list
    // knows, and the notification centre is a hook the frame holds. A third list joins this function.
    const listed = useListedMail();

    function press(): void {
        listed.readAgain();
        readNotificationsAgain();
    }

    return (
        <button
            type="button"
            aria-label={translate('shell.refresh')}
            className="flex flex-1 cursor-pointer flex-col items-center gap-0.75 rounded-2xl px-0.5 py-1.75 text-2xs font-medium text-muted transition hover:bg-hover hover:text-text workspace:size-8.5 workspace:flex-none workspace:justify-center workspace:gap-0 workspace:rounded-xl workspace:border workspace:border-line workspace:bg-panel workspace:px-0 workspace:py-0 workspace:text-muted workspace:hover:border-accent-line workspace:hover:bg-accent-soft workspace:hover:text-accent-deep"
            onClick={press}
        >
            <Icon name="refresh" className="size-5" />

            {/* The name is under the symbol wherever the navigation carries names, and gone in the rail, where the
                design draws this control as the symbol alone. */}
            <span className="max-w-full truncate workspace:hidden">{translate('shell.refresh')}</span>
        </button>
    );
}
