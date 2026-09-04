// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
    'shell.account': 'Konto i ustawienia',
    'shell.accountMenu': 'Konto',
    'shell.mailboxes': 'Skrzynki',
    'shell.tabMode': 'Tryb zakładek',
    'shell.tabModeTooNarrow': 'dostępne na szerszym ekranie',
    'control.notBuiltYet': '{control} — jeszcze niezbudowane',
    'ai.badge': 'AI',
    'confirm.undoableFor.one': 'Możesz to cofnąć jeszcze przez {count} sekundę.',
    'confirm.undoableFor.few': 'Możesz to cofnąć jeszcze przez {count} sekundy.',
    'confirm.undoableFor.many': 'Możesz to cofnąć jeszcze przez {count} sekund.',
    'confirm.undoableFor.other': 'Możesz to cofnąć jeszcze przez {count} sekund.',
    'proposal.offered': 'MailFathom to proponuje. Nic się jeszcze nie wydarzyło.',
    'proposal.reason': 'Dlaczego: {reason}',
    'proposal.impact': 'Co się zmieni: {impact}',
    'proposal.confirmed': 'Zanim cokolwiek się zmieni, poprosimy o potwierdzenie.',
    'proposal.unconfirmed': 'To nie zmienia niczego poza tym klientem, więc wydarzy się od razu po zgodzie.',
    'proposal.notNow': 'Nie teraz',

    'theme.automatic': 'Auto',
    'theme.light': 'Jasny',
    'theme.dark': 'Ciemny',

    'space.discover': 'Odkrywaj',
    'space.mail': 'Poczta',
    'space.cases': 'Sprawy',
    'space.agent': 'Agent',
    'space.tasks': 'Zadania',
    'space.calendar': 'Kalendarz',
    'space.people': 'Osoby',
    'space.notBuiltYet': '{space} — jeszcze niezbudowane',
    'space.pending':
        'Ta przestrzeń nie jest jeszcze zbudowana. Jest tu rama wokół niej: jej adres, nawigacja i zakres, w którym zadawane jest każde pytanie.',

    'intent.label': 'Zapytaj swoją pocztę',
    'intent.placeholder': 'O co chcesz zapytać swoją pocztę?',
    'intent.ask': 'Zapytaj',

    'scope.mailbox': 'Skrzynka w zakresie',
    'scope.allMailboxes': 'Wszystkie skrzynki',

    'signIn.claim': 'Twoja poczta zostaje na Twoim serwerze.',
    'signIn.claimExplanation':
        'Podaj adres serwera MailFathom swojej organizacji. Indeksowanie i analiza wiadomości dzieją się po jego stronie — nic nie trafia do chmury bez Twojej zgody.',
    'signIn.revealPassword': 'Pokaż',
    'signIn.hidePassword': 'Ukryj',
    'signIn.revealPasswordControl': 'Pokaż hasło',
    'signIn.hidePasswordControl': 'Ukryj hasło',

    'signIn.title': 'Połącz skrzynkę',
    'signIn.explanation': 'Dane logowania trafiają wyłącznie do wskazanego serwera.',
    'signIn.userName': 'Login',
    'signIn.userNameExample': 'k.kowalska@example.com',
    'signIn.password': 'Hasło',
    'signIn.submit': 'Połącz',
    'signIn.presenting': 'Łączenie z {address}…',
    'signIn.abandon': 'Przerwij próbę',
    'signIn.incomplete': 'Wpisz login i hasło do swojego wdrożenia.',
    'signIn.userNameHasColon':
        'Login nie może zawierać dwukropka, ponieważ to on oddziela go od hasła podczas wysyłania.',
    'signIn.tooLong':
        'Ten login lub to hasło są dłuższe, niż klient jest w stanie przedstawić. Sprawdź, co zostało wklejone.',
    'signIn.credentialRefused': 'To wdrożenie nie akceptuje tego loginu lub tego hasła.',
    'signIn.basicNotOffered':
        'To wdrożenie nie przyjmuje loginu i hasła. Osoba, która je prowadzi, musi najpierw włączyć taką możliwość.',
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

    'connect.address': 'Serwer',
    'connect.addressConfigured':
        'Adres serwera został podany przy instalacji tego klienta, więc nie można go tutaj zmienić.',
    'connect.addressExample': 'mailfathom.example.com:8443',
    'connect.addressHint': 'Port opcjonalny — bez niego ten klient łączy się na porcie {port}.',
    'connect.clearText': 'Łącz się z tym wdrożeniem zwykłym protokołem HTTP',
    'connect.clearTextConfigured':
        'To ustawienie zostało określone przy instalacji klienta, więc nie zmienisz go tutaj. Decyduje o nim osoba, która go skonfigurowała.',
    'connect.clearTextExplanation':
        'Twoje hasło jest kodowane, a nie szyfrowane, przy każdym żądaniu. Każdy, kto znajduje się między tym klientem a wdrożeniem, może je odczytać. Zostaw tę opcję wyłączoną, chyba że sieć między nimi należy do Ciebie.',
    'connect.clearTextInForce':
        'TLS jest wyłączony. Login, hasło i każda odczytana wiadomość pójdą otwartym tekstem. Używaj tego tylko w sieci, którą kontrolujesz, albo przez VPN.',
    'connect.portHint': 'port {port}',
    'connect.advanced': 'Zaawansowane',
    'connect.withoutTls': 'bez TLS',
    'connect.protocol': 'Protokół',
    'connect.protocolOverTls': 'HTTPS, przez TLS',
    'connect.protocolClearText': 'HTTP, bez szyfrowania',
    'connect.host': 'Adres',
    'connect.port': 'Port',
    'connect.portDefault': '{port} (domyślny)',
    'connect.certificate': 'Weryfikacja certyfikatu',
    'connect.certificateChecked': 'Wymagana',
    'connect.certificateNone': 'Brak — nic nie jest szyfrowane',
    'connect.nothingNamed': 'Nic jeszcze nie podano',
    'connect.blank': 'Podaj wdrożenie, które przechowuje Twoją pocztę.',
    'connect.malformed': 'To nie jest adres. Podaj host, na którym wdrożenie odpowiada, oraz port, jeśli go używa.',
    'connect.clearTextRefused':
        'Ten adres używa zwykłego protokołu HTTP, a ten klient nie wyśle nim hasła, dopóki na to nie zezwolisz.',
    'connect.unavailable': 'Nic tam nie odpowiedziało. Sprawdź adres oraz to, czy wdrożenie jest uruchomione.',
    'connect.unreadable': 'Coś tam odpowiedziało, ale nie jako MailFathom.',

    'configuration.refused': 'Ten klient jest błędnie skonfigurowany',
    'configuration.addressMalformed':
        'Podany adres serwera nie jest adresem. Musi to być host, na którym wdrożenie odpowiada, oraz port, jeśli go używa.',
    'configuration.addressNeedsClearTextPermission':
        'Podany adres serwera używa zwykłego protokołu HTTP, a nic nie zezwoliło na niezabezpieczone połączenie. Podaj adres https albo zezwól obok niego na otwarty tekst.',
    'configuration.clearTextContradictsAddress':
        'Zezwolono na niezabezpieczone połączenie i jednocześnie podano adres https, co jest dwiema różnymi odpowiedziami na jedno pytanie. Usuń tę z nich, która jest błędna.',
    'configuration.permissionNotABoolean':
        'Zezwolenie na niezabezpieczone połączenie musi mieć wartość true albo false, a podana wartość nie jest żadną z nich.',
    'configuration.whereItIsStated':
        'Oba ustawienia są odczytywane z argumentów uruchomienia MailFathom, z jego środowiska oraz z pliku client.conf obok jego własnej konfiguracji — w tej kolejności.',

    'preferences.notStated':
        'Ta zmiana nie została zapisana na wdrożeniu, więc obowiązuje tylko na tym urządzeniu do czasu, aż kolejna się powiedzie.',

    'settings.title': 'Ustawienia',
    'settings.close': 'Zamknij ustawienia',
    'settings.sections': 'Sekcje ustawień',
    'settings.profile': 'Profil',
    'settings.application': 'Aplikacja',
    'settings.name': 'Imię i nazwisko',
    'settings.nameNotYours':
        'Twoje imię i nazwisko prowadzi ten, kto zarządza tym wdrożeniem, więc jest tu pokazane, a nie oddane do zmiany.',
    'settings.nameNotAcceptable':
        'To imię i nazwisko nie zostało przyjęte. Nie może być puste ani dłuższe niż 128 znaków i nie może być takie, jakim posługuje się już ktoś inny na tym wdrożeniu.',
    'settings.nameNotStored':
        'To imię i nazwisko nie zostało zapisane na wdrożeniu, więc nadal prowadzi Cię pod poprzednim.',
    'settings.choosePicture': 'Zdjęcie',
    'settings.removePicture': 'Usuń',
    'settings.pictureBounds': 'JPG/PNG, maks. 1 MB',
    'settings.pictureNotAnImageKind': 'Ten plik nie jest ani plikiem JPEG, ani PNG, więc nie został wysłany.',
    'settings.pictureTooLarge': 'To zdjęcie jest większe niż 1 MB, więc nie zostało wysłane.',
    'settings.pictureNotStored':
        'To zdjęcie nie zostało zapisane na wdrożeniu, więc nadal jesteś rysowany tak jak wcześniej.',
    'settings.profileHeld':
        'Imię, nazwisko i zdjęcie przechowuje wdrożenie, na które się logujesz, więc idą za Tobą między maszynami. Żadne z nich nie trafia na Twój serwer poczty.',
    'settings.messageView': 'Widok wiadomości',
    'settings.messageViewReduced': 'Uproszczony',
    'settings.messageViewHtml': 'HTML',
    'settings.messageViewReducedExplanation':
        'Wiadomości pokazują oczyszczony tekst; pełny HTML otwierasz ikoną przy nagłówku.',
    'settings.messageViewHtmlExplanation':
        'Wiadomości pokazują osadzoną treść HTML; przełącznik HTML przy nagłówku wiadomości znika.',
    'settings.messageViewHtmlWarning':
        'Ryzyko bezpieczeństwa: HTML z wiadomości może zawierać ukryte piksele śledzące, podszywające się układy i odnośniki phishingowe. Renderujemy go w izolacji, ale sam podgląd może ujawnić nadawcy, że wiadomość została otwarta.',
    'settings.expandWholeThread': 'Rozwijaj cały wątek automatycznie',
    'settings.expandWholeThreadExplanation':
        'Bez tego wątek otwiera się na wybranej wiadomości, a pozostałe są pod przyciskiem.',
    'settings.privacy': 'Prywatność',
    'settings.telemetryWithheld': 'Nie wysyłaj danych telemetrycznych',
    'settings.telemetryExplanation': 'Diagnostyka błędów i statystyki użycia zostają na tym urządzeniu.',
    'settings.telemetryWithheldWarning':
        'Bez telemetrii pomoc techniczna będzie utrudniona — zgłoszenia trzeba będzie opisywać ręcznie, a części błędów nie da się odtworzyć po stronie wsparcia.',
    'settings.telemetryDestination':
        'Wysyłane do {address}, które przekazuje je dalej do skonfigurowanego kolektora. Zawierają to, które ekrany otwierasz i jak długo się otwierały — nigdy Twojej poczty, adresów, folderów ani hasła.',
    'settings.telemetryNotForwarded':
        'To wdrożenie nie przekazuje telemetrii, więc ten klient jej nie wysyła i nie ma czego wyłączać.',
    'settings.telemetryUnanswered':
        'Czekamy, aż to wdrożenie powie, czy przekazuje telemetrię. Dopóki nie odpowie, obowiązuje Twoja własna decyzja.',

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

    'mailboxes.heading': 'Katalogi',
    'mailboxes.fold': 'Zwiń panel skrzynek',
    'mailboxes.unfold': 'Rozwiń panel skrzynek',
    'mailboxes.open': 'Katalogi i filtry',
    'mailboxes.close': 'Zamknij katalogi',

    'aiFilters.heading': 'Filtry AI',
    'aiFilters.needsDecision': 'Wymaga decyzji',
    'aiFilters.commitments': 'Zobowiązania',
    'aiFilters.deadlinesThisWeek': 'Terminy w tym tygodniu',

    'mail.toolbar': 'Działania na poczcie',
    'mail.compose': 'Nowa wiadomość',
    'mail.reply': 'Odpowiedz',
    'mail.replyAll': 'Odpowiedz wszystkim',
    'mail.forward': 'Prześlij dalej',
    'mail.archive': 'Archiwizuj',
    'mail.delete': 'Usuń',
    'mail.flag': 'Flaga',
    'mail.markUnread': 'Nieprzeczytana',
    'mail.move': 'Przenieś',
    'mail.selectMessages': 'Zaznacz wiadomości',
    'mail.backToList': 'Wróć do listy',
    'mail.listColumn': 'Lista wiadomości',
    'mail.readingColumn': 'To, co otwarte',
    'mail.listWidth': 'Szerokość listy wiadomości',
    'mail.listWidthHint':
        'Przeciągnij, aby zmienić szerokość listy. Dwuklik lub klawisz Home przywraca szerokość początkową.',

    'select.bar': 'Działania na zaznaczonych wiadomościach',
    'select.count.one': '{count} zaznaczona',
    'select.count.few': '{count} zaznaczone',
    'select.count.many': '{count} zaznaczonych',
    'select.count.other': '{count} zaznaczonych',
    'select.all': 'Zaznacz wszystkie',
    'select.clear': 'Wyczyść zaznaczenie',

    'act.archived': 'Zarchiwizowano',
    'act.deleted': 'Przeniesiono do kosza',
    'act.flagged': 'Oflagowano',
    'act.markedUnread': 'Oznaczono jako nieprzeczytane',
    'act.filed': 'Przeniesiono do „{folder}”',
    'act.messages.one': '{count} wiadomość',
    'act.messages.few': '{count} wiadomości',
    'act.messages.many': '{count} wiadomości',
    'act.messages.other': '{count} wiadomości',
    'act.undo': 'Cofnij',
    'act.undone': 'Przywrócono na miejsce',
    'act.failed': 'Nie wprowadziliśmy tej zmiany: {reason}.',
    'act.someNotChanged':
        'Części z tych wiadomości nie zmieniliśmy. Twoje wdrożenie nie udostępnia ich już tam, gdzie narysowała je lista.',
    'act.archiving': 'Archiwizujemy…',
    'act.deleting': 'Przenosimy do kosza…',
    'act.flagging': 'Flagujemy…',
    'act.markingUnread': 'Oznaczamy jako nieprzeczytaną…',
    'act.filing': 'Przenosimy…',
    'act.underway': '{control} — to już jest w drodze na Twój serwer pocztowy.',
    'act.notOffered': '{control} — to poświadczenie nie może zmieniać poczty na Twoim serwerze.',
    'act.nothingToActOn': '{control} — nic nie jest otwarte ani zaznaczone, czego miałoby to dotyczyć.',
    'act.noArchiveFolder': '{control} — to konto nie wskazuje katalogu archiwum, więc nie ma dokąd archiwizować.',
    'act.noTrashFolder': '{control} — to konto nie wskazuje kosza, więc nie ma dokąd usuwać.',
    'act.severalAccounts': '{control} — wiadomości z kilku kont nie przeniesiemy do jednego katalogu.',
    'act.noOtherFolder': '{control} — to konto nie ma innego katalogu, do którego można przenieść.',
    'act.foldersUnknown': '{control} — MailFathom nie odczytał Twoich katalogów, więc nie wie, dokąd to trafi.',
    'act.foldersNotRead': 'Nie odczytaliśmy Twoich katalogów: {reason}.',
    'act.readFoldersAgain': 'Spróbuj ponownie',
    'act.cancel': 'Anuluj',
    'act.deleteQuestion.one': 'Usunąć {count} wiadomość?',
    'act.deleteQuestion.few': 'Usunąć {count} wiadomości?',
    'act.deleteQuestion.many': 'Usunąć {count} wiadomości?',
    'act.deleteQuestion.other': 'Usunąć {count} wiadomości?',
    'act.deleteConsequence': 'Każdą z nich przenosimy do kosza konta, na którym się znajduje.',
    'act.deleteConfirm': 'Przenieś do kosza',
    'act.moveTitle': 'Przenieś do innego katalogu',
    'act.moveClose': 'Zamknij',

    'compose.titleNew': 'Nowa wiadomość',
    'compose.titleReply': 'Odpowiedź',
    'compose.titleReplyAll': 'Odpowiedź do wszystkich',
    'compose.titleForward': 'Przekazanie dalej',
    'compose.close': 'Zamknij wiadomość',
    'compose.reading': 'Odczytujemy wiadomość, na którą odpowiadasz…',
    'compose.from': 'Od',
    'compose.to': 'Do',
    'compose.cc': 'DW',
    'compose.bcc': 'UDW',
    'compose.showCopies': 'Dopisz kopię lub ukrytą kopię',
    'compose.copyHeaders': 'DW · UDW',
    'compose.addRecipient': 'dodaj odbiorcę…',
    'compose.removeRecipient': 'Usuń adres {address} z pola {header}',
    'compose.notAnAddress': 'To jeszcze nie jest adres. Adres wygląda tak: ktos@example.com.',
    'compose.alreadyAddressed': 'Adres {address} jest tu już wpisany.',
    'compose.tooManyAddresses': 'Jedno pole przyjmuje najwyżej {count} adresów.',
    'compose.subject': 'Temat',
    'compose.subjectPlaceholder': 'Temat wiadomości',
    'compose.subjectOfAnAnswer': 'Temat odpowiedzi zapisuje Twoje wdrożenie, więc nie edytujemy go tutaj.',
    'compose.words': 'Treść',
    'compose.wordsPlaceholder': 'Napisz wiadomość',
    'compose.attach': 'Załącz',
    'compose.attachedFiles': 'Załączone pliki',
    'compose.removeFile': 'Usuń plik {name}',
    'compose.saveDraft': 'Zapisz szkic',
    'compose.shortcutSends': 'Ctrl+Enter wysyła',
    'compose.send': 'Wyślij',
    'compose.sendAnyway': 'Wyślij mimo to',
    'compose.backToEditing': 'Wróć do pisania',
    'compose.confirmQuestion': 'Wysłać tę wiadomość?',
    'compose.confirmTo': 'Do: {addresses}',
    'compose.confirmCc': 'Kopia do: {addresses}',
    'compose.confirmBcc': 'Ukryta kopia do: {addresses}',
    'compose.confirmNobody': 'Wiadomość nie ma adresata.',
    'compose.confirmSubject': 'Temat: {subject}',
    'compose.confirmNoSubject': 'brak',
    'compose.confirmRecallable': 'Możesz ją wycofać, dopóki wdrożenie nie przekaże jej serwerowi poczty.',
    'compose.cautionNoRecipient': 'Nikt nie jest wpisany jako adresat, więc nie ma dokąd jej wysłać.',
    'compose.cautionNoSubject': 'Wiadomość pójdzie bez tematu.',
    'compose.cautionNoWords': 'Wiadomość pójdzie bez treści.',
    'compose.discardQuestion': 'Odrzucić tę wiadomość?',
    'compose.discardExplanation':
        'To, co zostało napisane, przepadnie razem ze szkicem, który trzyma dla niej Twoje wdrożenie.',
    'compose.discardIsFinal': 'Nic jej wcześniej nie zapisze, więc do tego tekstu nie ma powrotu.',
    'compose.discard': 'Odrzuć',
    'compose.saving': 'Zapisujemy szkic…',
    'compose.saved': 'Szkic zapisany w Twoich szkicach.',
    'compose.attaching': 'Załączamy plik {name}…',
    'compose.sending': 'Wysyłamy…',
    'compose.queued': 'Wiadomość czeka w kolejce do wysłania.',
    'compose.withdraw': 'Wycofaj',
    'compose.withdrawn': 'Wycofana, zanim poszła dalej.',
    'compose.alreadyBeingSent': 'Wysyłka już trwa, więc nie dało się jej wycofać.',
    'compose.pastRecall': 'Wiadomość została wysłana, więc nie da się jej wycofać.',
    'compose.noSuchSend': 'Twoje wdrożenie nie prowadzi już tej wysyłki.',
    'compose.offline':
        'Ta maszyna jest bez sieci. To, co piszesz, zostaje tutaj, a zapisać lub wysłać można to, gdy sieć wróci.',
    'compose.refusedSendingNotEnabled':
        'To wdrożenie nie wysyła poczty. Osoba prowadząca wdrożenie może włączyć wysyłkę.',
    'compose.refusedRecipient':
        'Twoje wdrożenie odrzuciło jeden z adresów. Osoba prowadząca wdrożenie decyduje, na jakie adresy może iść poczta.',
    'compose.refusedCeiling':
        'Osiągnięto limit wydatków, więc nic nie wychodzi, dopóki okno, w którym jest liczony, się nie przewinie albo osoba prowadząca wdrożenie go nie podniesie.',
    'compose.refusedContent':
        'Kontrola treści odrzuciła to, co niesie ta wiadomość. Zmiana treści albo załączników jest tym, co to zmieni.',
    'compose.refusedNotScanned':
        'Części tej wiadomości nie dało się sprawdzić, więc nie została wysłana. Usunięcie tego, czego nie dało się odczytać, jest tym, co to zmieni.',
    'compose.refusedScreeningUnavailable':
        'Kontrola treści nie odpowiada, więc nic nie wychodzi, dopóki nie odpowie. Wiadomość nadal tu jest.',
    'compose.refusedForAnotherReason':
        'Twoje wdrożenie odmówiło wysłania. Osoba prowadząca wdrożenie odczyta powód z jego dziennika.',
    'compose.failedUnauthenticated':
        'Ten klient nie jest już zalogowany. Zaloguj się ponownie — to, co zostało napisane, nadal tu jest.',
    'compose.failedUnauthorized':
        'To poświadczenie nie może tego zrobić w tym wdrożeniu. Osoba prowadząca wdrożenie może nadać takie uprawnienie.',
    'compose.failedUnavailable':
        'Twoje wdrożenie nie odpowiedziało. To, co zostało napisane, zostaje tutaj; spróbuj ponownie.',
    'compose.failedUnreadable':
        'Twoje wdrożenie odpowiedziało, ale klient nie potrafił nic z tą odpowiedzią zrobić. To usterka warta zgłoszenia.',

    'tabs.strip': 'Co jest otwarte',
    'tabs.close': 'Zamknij: {title}',
    'tabs.closeAll': 'Zamknij wszystko, co jest otwarte',
    'tabs.closeAllQuestion': 'Zamknąć wszystkie zakładki?',
    'tabs.closeAllOpen.one': 'Otwarta zakładka: {count}.',
    'tabs.closeAllOpen.few': 'Otwarte zakładki: {count}.',
    'tabs.closeAllOpen.many': 'Otwartych zakładek: {count}.',
    'tabs.closeAllOpen.other': 'Otwartych zakładek: {count}.',
    'tabs.closeAllDraft': 'Niewysłany szkic zostanie odrzucony.',
    'tabs.closeAllIsFinal': 'Nic nie otworzy ich z powrotem razem — każdą otwierasz ponownie z listy.',
    'tabs.closeAllConfirm': 'Zamknij wszystkie',
    'tabs.closeAllCancel': 'Anuluj',
    'tabs.nothingOpen': 'Nic nie jest otwarte',
    'tabs.nothingOpenExplanation': 'Wybierz wiadomość z listy — otworzy się jako własna zakładka.',
    'tabs.reopenLastRead': 'Otwórz ostatnio czytaną wiadomość',

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

    'list.label': 'Wiadomości',
    'list.reading': 'Wczytywanie poczty…',
    'list.readingMore': 'Wczytywanie dalszych wiadomości…',
    'list.rowArriving': 'Ponowne wczytywanie tej wiadomości…',
    'list.wholeFolderRead': 'To już cały ten folder.',
    'list.failed': 'Nie udało się odczytać tego folderu: {reason}.',
    'list.partiallyFailed': 'Części tego folderu nie udało się odczytać: {reason}.',
    'list.emptyFolder': 'W tym folderze nie ma poczty.',
    'list.nothingMatches': 'Żadna wiadomość w tym folderze nie pasuje do zawężenia listy.',
    'list.notSynchronizedYet':
        'Do tego wdrożenia nie pobrano jeszcze niczego z tej skrzynki, więc nie ma czego pokazać. Folder nie jest pusty — nie został odczytany.',
    'list.emptyWhileFailing':
        'Ta skrzynka przestała się synchronizować, więc może tu być mniej wiadomości, niż ma serwer pocztowy.',

    'list.filters': 'Filtry',
    'list.filtersInForce': 'Aktywne filtry: {count}',
    'list.noFiltersInForce': 'Brak aktywnych filtrów',
    'list.clearFilters': 'Wyczyść filtry',
    'list.dateRange': 'Zakres dat',
    'list.rangeToday': 'Dziś',
    'list.rangeLastSevenDays': 'Ostatnie 7 dni',
    'list.rangeLastThirtyDays': 'Ostatnie 30 dni',
    'list.rangeThisYear': 'Ten rok',
    'list.receivedFromField': 'od',
    'list.receivedToField': 'do',
    'list.rangeSelectsNothing':
        'Koniec zakresu wypada przed jego początkiem, więc nic nie mogło przyjść pomiędzy nimi.',

    'list.order': 'Kolejność',
    'list.newestFirst': 'Najnowsze na górze',
    'list.oldestFirst': 'Najstarsze na górze',
    'list.onlyUnread': 'Tylko nieprzeczytane',
    'list.onlyFlagged': 'Tylko oznaczone',
    'list.onlyWithAttachments': 'Tylko z załącznikami',
    'list.includeJunk': 'Uwzględnij spam',

    'list.unread': 'Nieprzeczytana',
    'list.flagged': 'Oznaczona',
    'list.answered': 'Odpowiedziano',
    'list.attachments': 'Załączniki: {count}',
    'list.noSubject': 'Bez tematu',
    'list.senderUnknown': 'Brak nadawcy',

    'search.label': 'Znajdź wiadomość',
    'search.placeholder': 'Słowa z wiadomości, której szukasz',
    'search.blank': 'Wpisz, czego szukasz.',
    'search.submit': 'Szukaj',
    'search.stop': 'Zakończ wyszukiwanie',
    'search.tooLong': 'To jest dłuższe niż wyszukiwanie uruchamiane przez to wdrożenie, czyli {longest} znaków.',
    'search.recent': 'Wcześniej wyszukiwane',
    'search.forgetRecent': 'Zapomnij je',
    'search.everywhere': 'Przeszukiwana jest każda skrzynka i każdy folder.',
    'search.filters': 'Filtry nałożone na to wyszukiwanie',
    'search.remove': 'Usuń filtr {filter}',
    'search.narrow': 'Zawęź to wyszukiwanie',
    'search.addFilter': 'Dodaj',
    'search.everyAccount': 'Każda skrzynka',
    'search.everyFolder': 'Każdy folder',
    'search.notAnAddress': 'To nie jest adres. Wpisz go w całości, w postaci ktos@example.com.',
    'search.rangeSelectsNothing': 'Ostatni dzień wypada przed pierwszym, więc nic nie mogło przyjść pomiędzy nimi.',

    'search.narrowing.account': 'Skrzynka: {value}',
    'search.narrowing.folder': 'Folder: {value}',
    'search.narrowing.sender': 'Od {value}',
    'search.narrowing.recipient': 'Do {value}',
    'search.narrowing.receivedFrom': 'Przyszło {value} lub później',
    'search.narrowing.receivedTo': 'Przyszło {value} lub wcześniej',
    'search.narrowing.unread': 'Tylko nieprzeczytane',
    'search.narrowing.flagged': 'Tylko oflagowane',
    'search.narrowing.hasAttachments': 'Tylko z załącznikami',
    'search.narrowing.includeJunk': 'Ze spamem',

    'search.narrowing.accountField': 'W tej skrzynce',
    'search.narrowing.folderField': 'W tym folderze',
    'search.narrowing.senderField': 'Od tego adresu',
    'search.narrowing.recipientField': 'Do tego adresu',
    'search.narrowing.receivedFromField': 'Przyszło tego dnia lub później',
    'search.narrowing.receivedToField': 'Przyszło tego dnia lub wcześniej',

    'search.resultsLabel': 'Co znalazło to wyszukiwanie',
    'search.searching': 'Przeszukiwanie twojej poczty…',
    'search.readingMore': 'Wczytywanie kolejnych wyników…',
    'search.failed': 'Nie udało się przeprowadzić tego wyszukiwania: {reason}.',
    'search.partiallyFailed': 'Części tego wyszukiwania nie udało się odczytać: {reason}.',
    'search.nothingFound': 'Żadna wiadomość nie pasuje do tego wyszukiwania.',
    'search.widen': 'Przeszukaj zamiast tego całą pocztę',
    'search.wholeSearchRead': 'To wszystko, co znalazło to wyszukiwanie.',
    'search.mostResultsRead': 'Tak daleko sięga jedno wyszukiwanie. Zawęź je, aby znaleźć to, co leży dalej.',
    'search.whyItMatched': 'Dlaczego to pasuje:',
    'search.matchedByMeaning': 'Znalezione po znaczeniu, a nie po tych słowach.',
    'search.matchedInMail': 'Pasuje to, czego dotyczy ta wiadomość, a nie cokolwiek w jej treści.',
    'search.matchedBothWays': 'Pasują te słowa i to, czego dotyczy ta wiadomość.',
    'search.wordsOnlyInactive':
        'To wdrożenie nie wyszukuje po znaczeniu, więc te wyniki zawierają wpisane przez ciebie słowa i nic, co zostałoby znalezione wyłącznie po znaczeniu.',
    'search.wordsOnlyDegraded':
        'Wyszukiwanie po znaczeniu w tej chwili nie działa na tym wdrożeniu, więc te wyniki zawierają wyłącznie wpisane przez ciebie słowa. Osoba, która je prowadzi, może się temu przyjrzeć.',

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
    'grant.writeMailFlags':
        'To poświadczenie nie może zmieniać flag na Twoim serwerze poczty, więc otwarcie wiadomości pozostawia ją tam nieprzeczytaną, nie oferujemy flagowania ani oznaczania jako nieprzeczytanej, a klient pokazuje to, co serwer zgłosił ostatnio. Osoba prowadząca wdrożenie może nadać takie uprawnienie.',
    'grant.fileMail':
        'To poświadczenie nie może przenosić poczty do innego katalogu w tym wdrożeniu, więc nie oferujemy archiwizowania, usuwania ani przenoszenia. Osoba prowadząca wdrożenie może nadać takie uprawnienie.',

    'grant.composeMail':
        'To poświadczenie nie może zapisywać szkiców w tym wdrożeniu, więc nie oferujemy pisania wiadomości. Osoba prowadząca wdrożenie może nadać takie uprawnienie.',
    'grant.sendMail':
        'To poświadczenie nie może wysyłać poczty z tego wdrożenia, więc wiadomość można napisać i zapisać jako szkic, ale nie wysłać. Osoba prowadząca wdrożenie może nadać takie uprawnienie.',
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
    'body.quotedHistory': 'Rozmowa cytowana w tej wiadomości',
    'body.markupFitting': 'Dopasowywanie wysokości do treści…',
    'body.markupIsolated': 'Treść HTML nadawcy w izolacji — skrypty i zdalne zasoby zablokowane',
    'body.markupNotMeasured': 'Nie udało się zmierzyć wysokości treści — ta jedna ramka przewija się osobno',
    'body.markupAbsent': 'Nadawca nie napisał sformatowanej wersji tej wiadomości, więc pokazujemy wersję uproszczoną.',
    'body.markupTruncated':
        'Ta wiadomość jest dłuższa, niż zwraca jeden odczyt, więc zamiast wersji nadawcy pokazujemy uproszczoną.',
    'body.markupPicturesTruncated':
        'Część obrazów z tej wiadomości jest większa, niż zwraca jeden odczyt, więc zamiast wersji nadawcy pokazujemy uproszczoną.',

    'fullHtml.show': 'Pokaż pełną wersję HTML',
    'fullHtml.question': 'Pokazać pełny HTML?',
    'fullHtml.whatItCanCarry':
        'Ten kod napisał sam nadawca. Może zawierać piksele śledzące, układ podszywający się pod znaną markę i odnośniki prowadzące gdzie indziej, niż zapowiadają.',
    'fullHtml.whatIsBlocked':
        'Nic w nim nie może się wykonać i nic nie sięga do nadawcy, dopóki nie poprosisz o jego obrazy.',
    'fullHtml.stayReduced': 'Zostań przy wersji uproszczonej',
    'fullHtml.confirm': 'Pokaż HTML',
    'fullHtml.surface': 'Własna wersja tej wiadomości od nadawcy',
    'fullHtml.mark': 'HTML',
    'fullHtml.frame': 'Kod napisany przez nadawcę, rysowany w izolacji',
    'fullHtml.sentBy': '{author} · {when}',
    'fullHtml.close': 'Zamknij ten widok',
    'fullHtml.reading': 'Wczytywanie wersji od nadawcy…',
    'fullHtml.failed': 'Nie udało się odczytać wersji od nadawcy: {reason}.',
    'fullHtml.noMarkup': 'Nadawca nie napisał sformatowanej wersji tej wiadomości, więc nie ma tu czego pokazać.',
    'fullHtml.truncated': 'Ta wiadomość jest dłuższa, niż zwraca jeden odczyt, więc urywa się w tym miejscu.',
    'fullHtml.picturesTruncated':
        'Ta wiadomość niosła więcej własnych obrazów, niż mieści jeden widok, więc części z nich tu nie ma.',
    'fullHtml.cannotRun': 'Ta wiadomość nie może niczego wykonać: ramka, w której ją rysujemy, nie dopuszcza skryptów.',
    'fullHtml.reachesNobody':
        'Nie niesie żadnego adresu, który sięgałby do nadawcy — wszystkie usunięto, zanim ta wiadomość trafiła do klienta.',
    'fullHtml.picturesAsked':
        'Obrazy tej wiadomości są pobierane od nadawcy, więc jego serwery mogą rozpoznać, że ją otwarto. Nic z tego nie jest zapamiętywane: po wyjściu z wiadomości i powrocie pytamy ponownie.',

    'thread.label': 'Rozmowa',
    'thread.open': 'Pokaż całą rozmowę',
    'thread.close': 'Wróć do wiadomości',
    'thread.reading': 'Trwa wczytywanie tej rozmowy…',
    'thread.readingMore': 'Trwa wczytywanie dalszej części tej rozmowy…',
    'thread.readMore': 'Wczytaj dalszą część tej rozmowy',
    'thread.wholeConversationRead': 'To już cała ta rozmowa.',
    'thread.failed': 'Nie udało się odczytać tej rozmowy: {reason}.',
    'thread.partiallyFailed': 'Nie udało się odczytać części tej rozmowy: {reason}.',
    'thread.offline':
        'Ta maszyna jest bez sieci, więc nie można otworzyć tej rozmowy. Otworzy się sama, gdy sieć wróci.',
    'thread.empty': 'W tej rozmowie nie ma wiadomości, którą wolno ci zobaczyć.',
    'thread.messages': 'Wiadomości w tej rozmowie: {count}',
    'thread.wroteHere': 'Napisali: {names}',
    'thread.moreParticipants': 'W tej rozmowie pisało więcej osób, niż wymieniono tutaj.',
    'thread.moreNotAssembled': 'Ta rozmowa jest dłuższa, niż obejmuje jeden odczyt, więc pokazany jest jej początek.',
    'thread.storedIn': 'W koncie {account}, folder {folder}',
    'thread.openOnItsOwn': 'Otwórz tę wiadomość osobno',
    'thread.messageBy': 'Wiadomość od: {sender}',
    'thread.showEarlier': 'Pokaż wcześniejsze wiadomości ({count})',
    'thread.hideEarlier': 'Ukryj wcześniejsze wiadomości',
    'thread.openedFromList': 'Otwarta z listy',
    'thread.landedFromResult': 'Otwarta z wyniku wyszukiwania',

    'message.nothingOpen': 'Otwórz wiadomość, aby ją tutaj przeczytać.',
    'message.reading': 'Trwa otwieranie tej wiadomości…',
    'message.offline':
        'Ta maszyna jest bez sieci, więc nie można otworzyć tej wiadomości. Otworzy się sama, gdy sieć wróci.',
    'message.failed': 'Nie udało się otworzyć tej wiadomości: {reason}.',
    'message.noSubject': 'Bez tematu',
    'message.noAuthor': 'Ta wiadomość nie wskazuje nikogo jako autora.',
    'message.sentAt': 'Wysłano {when}',
    'message.sentAtUnknown': 'Nadawca nie zapisał daty, którą ten klient potrafi odczytać.',
    'message.receivedAt': 'Odebrano {when}',
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

    'attachments.list': 'Pliki dołączone do tej wiadomości',
    'attachments.downloadAll': 'Pobierz wszystkie',
    'attachment.unnamed': 'Plik bez nazwy',
    'attachment.open': 'Otwórz {name}',
    'attachment.close': 'Zamknij {name}',
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
    'attachment.reading': 'Trwa otwieranie pliku {name}…',
    'attachment.offline':
        'Ta maszyna jest bez sieci, więc nie można otworzyć tego pliku. Otworzy się sam, gdy sieć wróci.',
    'attachment.notShownUnauthenticated':
        'To wdrożenie nie przyjmuje już tych poświadczeń, więc nie udało się pokazać pliku. Zaloguj się ponownie.',
    'attachment.notShownUnauthorized':
        'Te poświadczenia nie mogą czytać poczty w tym wdrożeniu, więc nie udało się pokazać pliku.',
    'attachment.notShownUnavailable':
        'Wdrożenie nie odpowiedziało, więc nie udało się pokazać pliku. Spróbuj ponownie.',
    'attachment.notShownUnreadable':
        'To, co dotarło, nie zgadza się z tym, co ta wiadomość mówi o pliku, więc nic z niego nie rysujemy. Pobierz go i zgłoś to jako usterkę.',
    'attachment.empty': 'Ten plik nic nie zawiera.',
    'attachment.notShownKind':
        'Ten klient nie pokazuje plików tego rodzaju. Pobierz go, aby otworzyć w programie, który to potrafi.',
    'attachment.notShownSize':
        'Ten plik jest za duży, aby pokazać go tutaj. Pobierz go, aby otworzyć w programie, który to potrafi.',

    'carried.total': 'Wszystkie załączniki razem to {size}.',
    'carried.encrypted': 'Ta wiadomość zawiera gdzieś zaszyfrowaną treść.',
    'carried.unverifiedSignature': 'Ta wiadomość zawiera podpis, którego nic tutaj nie zweryfikowało.',
    'carried.unexpandedTnefPart':
        'Ta wiadomość zawiera część winmail.dat, którą zapisano bez otwierania, więc to, co się w niej znajduje, nie jest wymienione powyżej.',

    'scope.fragment': 'Pytanie dotyczy zaznaczonego fragmentu tej wiadomości: „{fragment}”',
    'scope.wholeMessage': 'Pytaj o całą wiadomość',

    'blocking.progress': 'Jak daleko zaszła operacja',
    'blocking.progressReading': '{percentage} — nie zamykaj tego okna',
    'blocking.noKnownFinish': 'Bez znanego czasu zakończenia',
    'blocking.doNotClose': 'Nie zamykaj aplikacji — operacja wciąż trwa.',
    'blocking.cancel': 'Anuluj',
    'blocking.stopQuestion': 'Na pewno przerwać?',
    'blocking.continue': 'Kontynuuj operację',
    'blocking.stop': 'Tak, przerwij',

    'link.goesTo': 'prowadzi do {host}',
    'link.warningDisplayedHostDiffers': 'Ten odnośnik nie prowadzi tam, gdzie mówią jego słowa. Prowadzi do {host}.',
    'link.warningAsciiHost': 'Ten odnośnik prowadzi do {host}, zapisanego jako {asciiHost}.',
    'link.warningWorthChecking': 'Ten odnośnik warto sprawdzić przed otwarciem. Prowadzi do {host}.',
    'link.couldNotOpen': 'Nie udało się otworzyć tego odnośnika.',

    'toast.surface': 'Komunikaty',
    'toast.close': 'Zamknij',
    'toast.neutral': 'Potwierdzenie',
    'toast.success': 'Sukces',
    'toast.error': 'Błąd',
    'toast.warning': 'Ostrzeżenie',
    'toast.info': 'Informacja',
    'toast.running': 'Operacja w toku',
    'toast.stopOperation': 'Przerwij operację',
    'toast.stopQuestion': 'Przerwać operację?',
    'toast.keepGoing': 'Kontynuuj',
    'toast.stopIsFinal': 'Przerwania nie da się cofnąć — operację trzeba by uruchomić od nowa.',
    'toast.stopped': 'Anulowano',
    'toast.stoppedNothingWritten': 'Operacja przerwana przed zapisem — nic nie zostało zmienione.',
};
