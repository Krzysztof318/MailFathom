// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// Who wrote a message, as the mark a conversation is scanned down rather than read. A thread is the one place several
// people alternate line after line, which is what the mark is for: it is recognised at a glance where a name has to be
// read, and it is why the mail list — one correspondent per row — draws none.
//
// It is decorative and says so. The name it stands for is on the same line, so a reader who cannot see the circle
// loses nothing, and announcing two letters before every sender would be noise rather than information.
//
// Nothing is invented to fill it. A message whose sender this deployment could not name draws no circle rather than a
// letter taken from something that is not a name.

/** The letters a sender is recognised by, or nothing where neither a name nor an address offers one. */
function initialsOf(displayName: string | null, address: string | null): string | null {
    const named = words(displayName);
    const first = named.at(0);
    const last = named.at(-1);

    if (first !== undefined && last !== undefined) {
        return named.length > 1 ? `${leading(first)}${leading(last)}` : leading(first);
    }

    const only = words(localPart(address)).at(0);

    return only === undefined ? null : leading(only);
}

/** What a sender called themselves, which is the part of an address a person is recognised by rather than the host. */
function localPart(address: string | null): string | null {
    const at = address?.indexOf('@') ?? -1;

    return address !== null && at > 0 ? address.slice(0, at) : address;
}

function words(text: string | null): readonly string[] {
    return (text ?? '').split(/[\s._-]+/u).filter((word) => /\p{L}|\p{N}/u.test(word));
}

function leading(word: string): string {
    return (Array.from(word)[0] ?? '').toUpperCase();
}

/** The circle a sender is recognised by in a conversation, drawn only where there are letters to put in it. */
export function SenderAvatar({
    displayName,
    address,
}: {
    readonly displayName: string | null;
    readonly address: string | null;
}) {
    const initials = initialsOf(displayName, address);

    if (initials === null) {
        return null;
    }

    return (
        <span
            aria-hidden="true"
            className="flex size-6 shrink-0 items-center justify-center self-center rounded-full bg-accent-soft text-xs font-semibold text-accent-strong"
        >
            {initials}
        </span>
    );
}
