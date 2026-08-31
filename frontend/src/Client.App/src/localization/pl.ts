// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { Catalogue } from './en';

// The one part of this repository that is deliberately not in English, which is why `.config/typos.toml` excludes this
// file from the spell check: a dictionary of English reads a Polish word as a misspelling of the English one it
// resembles. The annotation is what holds it to `en.ts` — a key missing here, or one written here that English does not
// declare, fails `pnpm typecheck`.

export const pl: Catalogue = {
    'shell.title': 'MailFathom',
    'shell.language': 'Język',
    'shell.theme': 'Motyw',
    'shell.spaces': 'Przestrzenie',

    'theme.system': 'Zgodnie z systemem',
    'theme.light': 'Jasny',
    'theme.dark': 'Ciemny',

    'space.discover': 'Odkrywaj',
    'space.mail': 'Poczta',
    'space.cases': 'Sprawy',
    'space.pending':
        'Ta przestrzeń nie jest jeszcze zbudowana. Jest tu rama wokół niej: jej adres, nawigacja i zakres, w którym zadawane jest każde pytanie.',

    'intent.label': 'Zapytaj swoją pocztę',
    'intent.placeholder': 'O co chcesz zapytać swoją pocztę?',

    'scope.mailbox': 'Skrzynka w zakresie',
    'scope.allMailboxes': 'Wszystkie skrzynki',

    'connect.title': 'Wskaż temu klientowi swój MailFathom',
    'connect.explanation':
        'Podaj wdrożenie, które przechowuje Twoją pocztę. Wszystko, co ten klient odczytuje, i wszystko, co w nim wpiszesz, trafia tam i nigdzie indziej.',
    'connect.address': 'Adres wdrożenia',
    'connect.addressHint':
        'Host, na którym wdrożenie odpowiada, oraz port, jeśli go używa — na przykład mailfathom.example.com albo mailfathom.example.com:8443.',
    'connect.clearText': 'Łącz się z tym wdrożeniem zwykłym protokołem HTTP',
    'connect.clearTextExplanation':
        'Twoje hasło jest kodowane, a nie szyfrowane, przy każdym żądaniu. Każdy, kto znajduje się między tym klientem a wdrożeniem, może je odczytać. Zostaw tę opcję wyłączoną, chyba że sieć między nimi należy do Ciebie.',
    'connect.submit': 'Połącz',
    'connect.reaching': 'Łączenie z wdrożeniem…',
    'connect.abandon': 'Przerwij próbę',
    'connect.blank': 'Podaj wdrożenie, które przechowuje Twoją pocztę.',
    'connect.malformed': 'To nie jest adres. Podaj host, na którym wdrożenie odpowiada, oraz port, jeśli go używa.',
    'connect.clearTextRefused':
        'Ten adres używa zwykłego protokołu HTTP, a ten klient nie wyśle nim hasła, dopóki na to nie zezwolisz.',
    'connect.unavailable': 'Nic tam nie odpowiedziało. Sprawdź adres oraz to, czy wdrożenie jest uruchomione.',
    'connect.unreadable': 'Coś tam odpowiedziało, ale nie jako MailFathom.',
    'connect.refused': 'Wdrożenie odrzuciło żądanie.',

    'deployment.reachedAt': 'Odczyt z {address}',
    'deployment.change': 'Wskaż inne wdrożenie',

    'accounts.reading': 'Odczytywanie kont…',
    'accounts.notRefreshing': 'To wdrożenie nie odświeża lokalnej kopii tych kont.',
    'accounts.failed': 'Nie udało się odczytać kont: {reason}.',

    'connection.current': 'Wszystkie konta są aktualne.',
    'connection.behind': 'Część kont ma zaległości.',
    'connection.noAccounts': 'Dla tego właściciela nie skonfigurowano jeszcze żadnego konta pocztowego.',
    'connection.retry': 'Spróbuj ponownie',

    'failure.unauthenticated': 'brak uwierzytelnienia',
    'failure.unauthorized': 'brak uprawnień',
    'failure.unavailable': 'usługa niedostępna',
    'failure.unreadable': 'odpowiedź nie do odczytania',
};
