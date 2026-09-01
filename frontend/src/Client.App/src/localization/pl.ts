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
    'shell.signOut': 'Wyloguj się',
    'shell.clientVersion': 'Klient {client}',
    'shell.versions': 'Klient {client}, wdrożenie {deployment}',

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

    'signIn.title': 'Zaloguj się do swojego MailFathom',
    'signIn.explanation':
        'Wszystko, co ten klient odczytuje, i wszystko, co w nim wpiszesz, trafia do wdrożenia przechowującego Twoją pocztę i nigdzie indziej.',
    'signIn.userName': 'Nazwa użytkownika',
    'signIn.password': 'Hasło',
    'signIn.submit': 'Zaloguj się',
    'signIn.presenting': 'Logowanie…',
    'signIn.abandon': 'Przerwij próbę',
    'signIn.incomplete': 'Wpisz nazwę użytkownika i hasło do swojego wdrożenia.',
    'signIn.userNameHasColon':
        'Nazwa użytkownika nie może zawierać dwukropka, ponieważ to on oddziela ją od hasła podczas wysyłania.',
    'signIn.tooLong':
        'Ta nazwa użytkownika lub to hasło są dłuższe, niż klient jest w stanie przedstawić. Sprawdź, co zostało wklejone.',
    'signIn.credentialRefused': 'To wdrożenie nie akceptuje tej nazwy użytkownika lub tego hasła.',
    'signIn.basicNotOffered':
        'To wdrożenie nie przyjmuje nazwy użytkownika i hasła. Osoba, która je prowadzi, musi najpierw włączyć taką możliwość.',
    'signIn.grantMissing': 'Wdrożenie przyjęło poświadczenie, ale nie zezwala mu na odczyt żadnej poczty.',
    'signIn.deploymentSilent': 'Wdrożenie nie odpowiedziało. Spróbuj ponownie za chwilę.',
    'signIn.noLongerAccepted': 'To wdrożenie przestało akceptować zapamiętane hasło. Zaloguj się ponownie.',
    'signIn.notRemoved':
        'Wylogowanie nie usunęło hasła z magazynu poświadczeń tej maszyny, więc nadal jest tam przechowywane. Usuń je w samym magazynie albo zaloguj się i wyloguj ponownie.',
    'signIn.notKept':
        'Nie udało się zapisać Twojego hasła na tej maszynie, więc zapytamy o nie ponownie przy następnym otwarciu MailFathom. Jesteś zalogowany tak czy inaczej.',
    'signIn.keptUntilSignedOut':
        'Twoje hasło jest przechowywane w pęku kluczy tego komputera, dopóki się nie wylogujesz. Wylogowanie jest tym, co je usuwa.',
    'signIn.keptUntilTheTabCloses':
        'Twoje hasło jest przechowywane do zamknięcia tej karty i zapytamy o nie ponownie — hasło pozostawione w przeglądarce może odczytać wszystko, co ma dostęp do tej strony.',
    'signIn.keptUntilTheClientCloses':
        'Twoje hasło jest przechowywane do zamknięcia MailFathom i zapytamy o nie ponownie — ten komputer nie udostępnia pęku kluczy, w którym można je bezpiecznie przechować.',

    'connect.address': 'Adres wdrożenia',
    'connect.addressHint':
        'Host, na którym wdrożenie odpowiada, oraz port, jeśli go używa — na przykład mailfathom.example.com albo mailfathom.example.com:8443.',
    'connect.clearText': 'Łącz się z tym wdrożeniem zwykłym protokołem HTTP',
    'connect.clearTextExplanation':
        'Twoje hasło jest kodowane, a nie szyfrowane, przy każdym żądaniu. Każdy, kto znajduje się między tym klientem a wdrożeniem, może je odczytać. Zostaw tę opcję wyłączoną, chyba że sieć między nimi należy do Ciebie.',
    'connect.blank': 'Podaj wdrożenie, które przechowuje Twoją pocztę.',
    'connect.malformed': 'To nie jest adres. Podaj host, na którym wdrożenie odpowiada, oraz port, jeśli go używa.',
    'connect.clearTextRefused':
        'Ten adres używa zwykłego protokołu HTTP, a ten klient nie wyśle nim hasła, dopóki na to nie zezwolisz.',
    'connect.unavailable': 'Nic tam nie odpowiedziało. Sprawdź adres oraz to, czy wdrożenie jest uruchomione.',
    'connect.unreadable': 'Coś tam odpowiedziało, ale nie jako MailFathom.',

    'deployment.reachedAt': 'Odczyt z {address}',
    'deployment.change': 'Wskaż inne wdrożenie',

    'accounts.reading': 'Odczytywanie kont…',
    'accounts.notRefreshing':
        'To wdrożenie nie odświeża lokalnej kopii tych kont, więc widzisz je w takim stanie, w jakim zostawił je jego ostatni przebieg. To ustawienie wdrożenia, a nie brakujące Ci uprawnienie.',
    'accounts.failed': 'Nie udało się odczytać kont: {reason}.',
    'accounts.oldest': 'Najstarsze z nich odświeżono {age}.',
    'accounts.noneDeclared':
        'To osoba prowadząca to wdrożenie wskazuje skrzynki, które są dla Ciebie odczytywane, a nie wskazano jeszcze żadnej.',

    'account.synchronized': 'Aktualne',
    'account.behind': 'Nadrabia zaległości',
    'account.failing': 'Przestało się synchronizować',
    'account.unreachable': 'Serwer pocztowy nie odpowiedział',
    'account.neverSynchronized': 'Nic jeszcze nie pobrano',
    'account.lastRefreshed': 'Ostatnio odświeżono {age}',
    'account.neverRefreshed': 'Nigdy nie odświeżono',

    'folders.label': 'Skrzynki i foldery',
    'folders.reading': 'Odczytywanie skrzynek i folderów…',
    'folders.failed': 'Nie udało się odczytać skrzynek i folderów: {reason}.',
    'folders.unread': 'nieprzeczytane: {count}',
    'folders.stored': 'przechowywane tutaj: {count}',

    'folder.inbox': 'Odebrane',
    'folder.drafts': 'Kopie robocze',
    'folder.sent': 'Wysłane',
    'folder.archive': 'Archiwum',
    'folder.junk': 'Spam',
    'folder.trash': 'Kosz',
    'folder.flagged': 'Oznaczone',
    'folder.important': 'Ważne',
    'folder.all': 'Cała poczta',
    'folder.outbox': 'Do wysłania',

    'connection.current': 'Wszystkie konta są aktualne.',
    'connection.behind': 'Część kont ma zaległości.',
    'connection.failing': 'Część kont przestała się synchronizować.',
    'connection.noAccounts': 'Dla tego właściciela nie skonfigurowano jeszcze żadnego konta pocztowego.',
    'connection.retry': 'Spróbuj ponownie',
    'connection.connecting': 'Łączenie z Twoim wdrożeniem…',
    'connection.reconnecting': 'Twoje wdrożenie nie odpowiedziało. Próbujemy ponownie — próba {attempt} z {total}.',
    'connection.lost': 'Twoje wdrożenie nie odpowiedziało po {total} próbach.',
    'connection.unreadable': 'Twoje wdrożenie odpowiedziało, ale klient nie mógł wykorzystać tej odpowiedzi: {reason}.',
    'connection.offline': 'Ta maszyna nie ma połączenia z siecią. Klient połączy się ponownie sam, gdy sieć wróci.',

    'grant.heading': 'Czego to poświadczenie nie może tutaj zrobić',
    'grant.readMail':
        'To poświadczenie nie może odczytywać poczty w tym wdrożeniu, więc nie pokazujemy żadnej skrzynki ani wiadomości. Osoba prowadząca wdrożenie może nadać takie uprawnienie.',
    'grant.askMail':
        'To poświadczenie nie może zadawać pytań Twojej poczcie w tym wdrożeniu, więc nie oferujemy pytania. Osoba prowadząca wdrożenie może nadać takie uprawnienie.',

    'failure.unauthenticated': 'brak uwierzytelnienia',
    'failure.unauthorized': 'brak uprawnień',
    'failure.unavailable': 'usługa niedostępna',
    'failure.unreadable': 'odpowiedź nie do odczytania',

    'body.reading': 'Odczytywanie wiadomości…',
    'body.failed': 'Nie udało się odczytać wiadomości: {reason}.',

    'body.encryptedNotReadable': 'Ta wiadomość jest zaszyfrowana i to wdrożenie nie potrafi jej odczytać.',
    'body.notStoredExceededSizeLimit':
        'Ta wiadomość była większa, niż to wdrożenie przechowuje, więc jej treść nie została zapisana.',
    'body.notStoredAwaitingStorageHeadroom':
        'Ta wiadomość czeka na miejsce w magazynie, zanim jej treść zostanie zapisana.',

    'body.refusedNoHtmlPart':
        'Nadawca nie napisał sformatowanej wersji tej wiadomości, więc jest ona pokazana jako sam tekst.',
    'body.refusedReductionFailed':
        'To wdrożenie nie potrafiło odczytać sformatowanej wersji tej wiadomości, więc jest ona pokazana jako sam tekst.',
    'body.refusedNothingRenderable':
        'Sformatowana wersja tej wiadomości nie zawierała niczego do narysowania, więc jest ona pokazana jako sam tekst.',
    'body.notReduced':
        'To wdrożenie nie przysłało wersji tej wiadomości do narysowania, więc jest ona pokazana jako sam tekst.',

    'body.truncated': 'Ta wiadomość jest dłuższa, niż okno czytania rysuje, więc kończy się w tym miejscu.',
    'body.textTruncated': 'Tekst tej wiadomości został skrócony przez ograniczenie stosowane przez to wdrożenie.',
    'body.blockNotDrawn': 'Fragment tej wiadomości powstał dla nowszego klienta niż ten, więc nie został narysowany.',
    'body.tableRegion': 'Tabela w tej wiadomości, przewijana w poziomie',
    'body.preformattedRegion': 'Tekst preformatowany w tej wiadomości, przewijany w poziomie',
    'body.pictureWithoutDescription': 'Obraz, którego nadawca nie opisał',

    'body.remoteContentRemoved':
        'Ta wiadomość prosiła o pobranie treści z innego serwera. Zostało to usunięte, więc otwarcie jej nic nadawcy nie zgłosiło.',
    'body.remoteContentRemovedCount': 'Usuniętych odwołań: {count}',
    'body.showRemotePictures': 'Pobierz obrazy od nadawcy',
    'body.showRemotePicturesReveals':
        'Pobranie ich mówi nadawcy, że ta wiadomość została otwarta. Pytanie dotyczy tylko tej wiadomości i nie jest nigdzie zapamiętywane.',
    'body.remotePicturesLoading': 'Trwa pobieranie…',
    'body.showWithoutRemotePictures': 'Pokaż wiadomość bez nich',
    'body.remotePicturesShown': 'Dla tej wiadomości obrazy są pobierane od nadawcy.',
    'body.remotePicturesShownCount': 'Obrazów pobranych od nadawcy: {count}',
    'body.undrawnPicturesCount': 'Obrazów zbyt dużych, by je narysować: {count}',

    'message.nothingOpen': 'Otwórz wiadomość, aby ją tutaj przeczytać.',
    'message.reading': 'Trwa otwieranie tej wiadomości…',
    'message.offline':
        'Ta maszyna jest bez sieci, więc nie można otworzyć tej wiadomości. Otworzy się sama, gdy sieć wróci.',
    'message.failed': 'Nie udało się otworzyć tej wiadomości: {reason}.',
    'message.noSubject': 'Bez tematu',
    'message.noAuthor': 'Ta wiadomość nie wskazuje nikogo jako autora.',
    'message.sentAt': 'Wysłano {when}',
    'message.sentAtUnknown': 'Nadawca nie zapisał daty, którą ten klient potrafi odczytać.',
    'message.otherParticipants': 'Pozostałe osoby wskazane w tej wiadomości ({count})',

    'participant.sender': 'Nadane przez',
    'participant.replyTo': 'Odpowiedź do',
    'participant.to': 'Do',
    'participant.cc': 'Kopia do',
    'participant.bcc': 'Ukryta kopia do',

    'sender.failed':
        'Odbierający serwer pocztowy sprawdził, kto naprawdę wysłał tę wiadomość, i stwierdził, że wskazany w niej autor się nie potwierdził.',
    'sender.recognized': 'To wdrożenie rozpoznaje nadawcę tej wiadomości.',
    'sender.authenticatedBy':
        'Uwierzytelniona przez {domain} — to ona naprawdę ją wysłała, a nie nazwa widoczna powyżej.',
    'sender.authenticatedByNobody': 'Nic nie uwierzytelniło nadawcy tej wiadomości.',

    'attachments.heading': 'Pliki dołączone do tej wiadomości',
    'attachment.unnamed': 'Plik bez nazwy',
    'attachment.download': 'Pobierz {name}',
    'attachment.nameWasRewritten':
        'Nadawca zapisał nazwę pliku, której to wdrożenie by nie użyło, więc widoczna jest nazwa nadana w jej miejsce.',
    'attachment.arriving': 'Jaka część pliku już dotarła',
    'attachment.arrivingOf': '{arrived} z {whole}',
    'attachment.stop': 'Przerwij pobieranie',
    'attachment.saved': 'Pobrano {name}.',
    'attachment.abandoned': 'Pobieranie zostało przerwane, więc nic nie zostało zapisane.',
    'attachment.refusedUnauthenticated':
        'To wdrożenie nie przyjmuje już tych danych logowania, więc plik nie został pobrany. Zaloguj się ponownie.',
    'attachment.refusedUnauthorized':
        'Te dane logowania nie pozwalają czytać poczty w tym wdrożeniu, więc plik nie został pobrany.',
    'attachment.refusedUnavailable': 'Wdrożenie nie odpowiedziało, więc plik nie został pobrany. Spróbuj ponownie.',
    'attachment.refusedLargerThanDescribed':
        'Wdrożenie przysłało więcej, niż ta wiadomość deklaruje dla tego pliku, więc nic nie zostało zapisane. Zgłoś to jako usterkę.',

    'carried.total': 'Wszystkie załączniki razem to {size}.',
    'carried.encrypted': 'Ta wiadomość zawiera gdzieś zaszyfrowaną treść.',
    'carried.unverifiedSignature': 'Ta wiadomość zawiera podpis, którego nic tutaj nie zweryfikowało.',
    'carried.unexpandedTnefPart':
        'Ta wiadomość zawiera część winmail.dat, którą zapisano bez otwierania, więc to, co się w niej znajduje, nie jest wymienione powyżej.',

    'scope.fragment': 'Pytanie dotyczy zaznaczonego fragmentu tej wiadomości: „{fragment}”',
    'scope.wholeMessage': 'Pytaj o całą wiadomość',

    'link.goesTo': 'prowadzi do {host}',
    'link.warningDisplayedHostDiffers': 'Ten odnośnik nie prowadzi tam, gdzie mówią jego słowa. Prowadzi do {host}.',
    'link.warningAsciiHost': 'Ten odnośnik prowadzi do {host}, zapisanego jako {asciiHost}.',
    'link.warningWorthChecking': 'Ten odnośnik warto sprawdzić przed otwarciem. Prowadzi do {host}.',
    'link.couldNotOpen': 'Nie udało się otworzyć tego odnośnika.',
};
