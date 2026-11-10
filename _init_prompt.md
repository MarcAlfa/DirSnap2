Ran inspect DirSnap and MusicAtlas2 directories
Ran read DirSnap and MusicAtlas2 structure and docs
Analyzed readme.md:L1-162
Analyzed _init_prompt.md:L1-150
Analyzed MusicAtlas2.csproj:L1-45
Analyzed app.py:L1-300
Analyzed editor_view.py:L1-300
Analyzed exporter.py:L1-83
Analyzed config.py:L1-124
Analyzed app.py:L301-650
Analyzed tree.py:L1-269
Analyzed tabs.py:L1-136
Analyzed app.py:L651-1359

Ecco il prompt completo e pronto all'uso da incollare nella nuova chat per far partire i lavori su **DirSnap 2**:

```markdown
Ciao! Voglio sviluppare qui (`F:\MARCALFA\APP\DirSnap2`) **DirSnap 2**, la nuova versione nativa per Windows 11 di **DirSnap** (`F:\MARCALFA\PYTHON\DirSnap`), utilizzando lo stesso stack tecnologico C# / .NET 8 / WinUI 3 (Windows App SDK, modalità Unpackaged con Mica, Fluent Design e dark/light mode) già adottato in `F:\MARCALFA\APP\MusicAtlas2`.

Prima di iniziare a scrivere codice:
1. Analizza direttamente il codebase di riferimento in `F:\MARCALFA\PYTHON\DirSnap` per recepire tutte le logiche esistenti (navigazione, tab workspace, auto-refresh, editor `.md/.txt/.json`, `Unlock` read-only per altri file di testo, formattazione JSON, salvataggio encoding/newline originali, esportazione liste e persistenza `config.json`) e guarda `F:\MARCALFA\APP\MusicAtlas2\MusicAtlas2.csproj` per replicare la configurazione del progetto WinUI 3.
2. **Tutta l'interfaccia utente (UI), etichette, menu contestuali, tooltip e messaggi devono essere rigorosamente in lingua INGLESE** (niente stringhe in italiano come "Copia" o "Apri in nuova scheda" presenti nella v1: usa "Copy", "Open in New Tab", ecc.).

### Novità e Redesign UI rispetto a DirSnap v1

Voglio eliminare la vecchia separazione tra le due viste ("Lists" ed "Editor") e unificare tutto attorno all'attuale **Editor View**, introducendo queste modifiche chiave:

1. **Unica Vista Principale (Unified Editor View) + Switch "Flat / Tree" a Sinistra**:
   - Non esiste più lo switch "Lists / Editor" nell'header. Ogni scheda (Tab, max 10, con salvataggio/ripristino completo del workspace in `config.json` e drag-and-drop) mostra direttamente il layout a due pannelli ridimensionabili (Splitter): **Pannello Navigazione/File a sinistra** + **Editor Testuale a destra**.
   - Nel pannello di sinistra aggiungi uno **switch rapido (`Flat` / `Tree`)**:
     - **Modalità `Flat` (default, come l'attuale Editor View)**: mostra `..` per salire, le sottocartelle e i file della cartella corrente con colonne ordinabili (`Name`, `Size`, `Modified`), filtro rapido e checkbox `show all`.
     - **Modalità `Tree`**: trasforma il pannello di sinistra nell'albero gerarchico completo del drive (lazy loading istantaneo) espandibile, mostrando **sia le cartelle sia i file contenuti nelle cartelle espanse**, così da poter navigare l'alberatura ed editare o raccogliere file/cartelle direttamente dall'albero.

2. **Nuovo Widget "📋 Snap List" (al posto della vecchia sezione Lists)**:
   - La raccolta ed esportazione di nomi di file e cartelle rimane, ma funziona come il **carrello di un e-commerce**, chiamato **`📋 Snap List`**.
   - **Globale**: La `Snap List` è unica e condivisa tra tutte le schede aperte.
   - **Come si aggiungono elementi alla Snap List**:
     1. Pulsante rapido `[+]` su ogni riga (visibile all'hover o a lato) nella lista/albero di sinistra per aggiungere al volo un file o una cartella senza cambiare focus.
     2. Menu contestuale tasto destro (`Add to Snap List`, supportando anche la selezione multipla) + scorciatoia da tastiera (es. `Ins` o tasto dedicato).
     3. Due pulsanti contatori rapidi nella barra del pannello sinistro (`+ N folders` e `+ N files`, attivi e colorati quando > 0) per aggiungere in un colpo solo tutte le sottocartelle o tutti i file della cartella corrente.
   - **Widget Header + Flyout & Finestra Staccabile (`Detach`)**:
     - Nell'header principale (accanto ai controlli della barra indirizzo) c'è il pulsante/widget **`📋 Snap List (N)`** con badge del conteggio elementi.
     - Cliccandolo si apre un **Flyout / Pop-up moderno** dove posso:
       - Vedere tutti gli elementi raccolti, rimuoverne singoli (doppio clic, tasto `Del` o icona cestino/x) o svuotare tutto (`Clear All`).
       - Attivare/disattivare i toggle di formattazione in tempo reale: `full path` e `with extension`.
       - Eseguire le azioni di export: **`📝 Send to Notepad`**, **`📋 Copy to Clipboard`**, **`💾 Export to .txt`**, **`📊 Export to .csv`**.
       - Cliccare su un pulsante **`📌 Detach`** (o Pin) nell'angolo del popup per staccare la `Snap List` in una **finestrina flottante sempre visibile** (e ri-ancorarla quando chiusa), comodissima mentre si naviga tra più schede.

Procedi pure esaminando `F:\MARCALFA\PYTHON\DirSnap` e `F:\MARCALFA\APP\MusicAtlas2`, imposta l'architettura di `DirSnap2` e avvia lo sviluppo!
```