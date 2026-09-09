// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The one on-or-off control the client draws, in both places the design project puts one: the tab mode in the account
// menu and the telemetry decision on the settings screen. One component rather than two similar arrangements of
// utilities, because the second screen drawing the same shape from its own copy is how a client stops looking like one
// product — and because the track and the knob are the same three decisions each time.
//
// The platform has no switch element, so a checkbox carries the role: `switch` is what says this is on or off rather
// than ticked, and everything a checkbox already gives — the keyboard, the label around it, the disabled state — is
// kept. The track and the knob are drawn from the checked state of the input beside them rather than from a second
// copy of it held in React.
//
// It renders no label of its own. Each caller wraps it in the `label` that names it, because what the switch decides
// is worded differently in each place and a name passed through here would be a sentence assembled away from the
// screen that says it.

export function Switch({
    on,
    onChange,
    disabled = false,
}: {
    readonly on: boolean;
    readonly onChange: (on: boolean) => void;
    readonly disabled?: boolean;
}) {
    return (
        // The input is hidden by being taken out of the flow, so it is positioned against whichever ancestor is
        // positioned — and a scrolling panel that is not one leaves it standing at its static position outside every
        // clip between here and there, which the panel's own scroller then reports as overflow nobody can see. So the
        // control carries the containing block itself rather than each screen remembering to, and the wrapper is what
        // keeps the track the input's next sibling, which is what `peer-checked` reads.
        <span className="relative flex shrink-0">
            <input
                type="checkbox"
                role="switch"
                checked={on}
                disabled={disabled}
                className="peer sr-only"
                onChange={(event) => {
                    onChange(event.target.checked);
                }}
            />
            <span className="flex w-7.5 shrink-0 items-center rounded-full bg-line-strong p-0.5 transition peer-checked:justify-end peer-checked:bg-accent peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-accent">
                <span className="size-3.25 rounded-full bg-panel" />
            </span>
        </span>
    );
}
