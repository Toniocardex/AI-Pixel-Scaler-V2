# AI Pixel Scaler — Best Tips

Guida pratica alle sequenze operative consigliate per ottenere la qualità migliore su immagini pixel art.

---

## 1. Rimozione sfondo — pipeline completa senza artefatti

La rimozione sfondo in un solo clic funziona bene per sfondi uniformi e ben separati.
Per risultati precisi su sprite con bordi complessi o sfondo a tinta variabile, segui questa sequenza:

### Sequenza consigliata

```
1. Contagocce ◉  →  campiona il colore sfondo direttamente sul canvas
2. Rimuovi sfondo    (tolleranza 8–15, protezione bordi 48)
3. Applica defringe  (soglia opaco 200–230)
4. [opzionale] Rimuovi sfondo di nuovo con tolleranza +5–10
5. Binarizza alpha   (soglia 128)
6. Isole → Denoise   (isole minime = 1–2 pixel)
```

### Perché ogni step

| Step | Cosa risolve |
|---|---|
| **Rimuovi sfondo** | Flood fill dai bordi immagine: rimuove la massa principale di sfondo connessa al bordo |
| **Applica defringe** | I pixel di bordo dello sprite hanno residui cromatici del colore sfondo (bleeding). Defringe li corregge spostando il colore verso l'interno dello sprite |
| **Secondo Rimuovi sfondo** | Cattura le "isole" di 1–3 pixel sfondo vicine al bordo, non raggiunte dal primo pass perché protette da un bordo Sobel forte |
| **Binarizza alpha** | La pixel art usa alpha binaria (0 o 255): questa operazione elimina i "pixel fantasma" a bassa alpha che sfuggono al flood |
| **Isole Denoise** | Rimuove pixel singoli rimasti isolati — rumore residuo non connesso a strutture sprite |

### Parametri di riferimento

| Parametro | Sfondo solido | Sfondo sfumato / anti-aliasing |
|---|---|---|
| Tolleranza colore | 8–12 | 15–25 |
| Protezione bordi | 48 | 32–40 |
| Soglia opaco defringe | 220 | 180 |
| Soglia binarizzazione alpha | 128 | 96–128 |
| Isole minime | 1 | 2–3 |

### Note operative

- **Usa sempre il contagocce ◉ inline** (pulsante accanto al campo colore nella sezione "Rimozione sfondo") per campionare direttamente il colore esatto dello sfondo. Inserire il valore hex a mano introduce errori se lo sfondo ha variazioni minime.
- **Esegui più run con colori diversi**: il flood è incrementale — i pixel già azzerati diventano corridoi per run successivi. Se lo sfondo ha due tinte (es. gradiente leggero), campiona la seconda tinta e ripeti "Rimuovi sfondo".
- **Non invertire l'ordine defringe/binarizza**: il defringe ha bisogno dei pixel semitrasparenti per correggere il colore. Se binarizzi prima, i pixel di bordo diventano già 0 o 255 e il defringe non ha nulla su cui lavorare.
- **Protezione bordi 0** = nessuna protezione Sobel: utile solo se lo sfondo è identico in colore a zone interne dello sprite (caso raro in pixel art). In tutti gli altri casi tenerla attiva.

---

## 2. Gomma — uso efficace su pixel art

La gomma cancella un quadrato N×N pixel, non un cerchio — coerente con la griglia pixel art.

### Scorciatoie

| Azione | Come |
|---|---|
| Cambia dimensione al volo | `Ctrl + rotella del mouse` sul canvas mentre la gomma è attiva |
| Singolo pixel | Preset `[1]` nella toolbar oppure `Ctrl+scroll` fino a 1 |
| Blocco 2×2 | Preset `[2]` |
| Cancella tile 4×4 | Preset `[4]` |
| Cancella area larga | Preset `[8]` |

### Consigli

- Per correzioni di precisione usa sempre gomma 1×1 (`[1]`): è il default all'avvio.
- Per cancellare sfondo rimasto in angoli interni (non raggiunto dal flood fill), usa gomma 1×1 con zoom alto invece di rilanciare la pipeline.
- `Ctrl + Z` annulla ogni singolo stroke della gomma — puoi annullare tratto per tratto anche in sessioni lunghe.

---

## 3. Sprite Studio — sequenza di pulizia rapida

Per sprite importati da spritesheet o screenshot di gioco:

```
1. Apri immagine (singolo sprite o spritesheet)
2. Sprite Studio → Step 2 "Pulizia"
3. One-click cleanup  →  applica preset Safe o Aggressive come punto di partenza
4. Regola manualmente: tolleranza, bordi, defringe
5. Esegui la pipeline "Rimozione sfondo" completa (vedi Tip 1) se necessario
6. Step 3 "Slice" → rileva celle automaticamente
7. Step 4 "Esporta" → PNG / ZIP / JSON
```

---

## 4. Tileset Studio — evitare artefatti seamless

Quando si crea un tileset seamless:

- Esegui **prima** la riduzione palette e poi il seamless — l'algoritmo di seamless lavora meglio su colori già quantizzati.
- Usa **Pad-to-multiple** prima di esportare per Tiled: assicura che le dimensioni siano multiple del tile size, evitando tile parziali.
- Il **template** va costruito dopo il seamless, non prima — altrimenti i bordi del template non si allineano ai bordi seamless.

---

## 5. Animation Studio — allineamento baseline

Per sprite con frame di altezze diverse (es. sprite che salta):

- Usa **Allinea tutti → Baseline** come prima operazione: radica tutti i frame sul suolo comune.
- Dopo la baseline, applica **Centra nelle celle** solo se i frame devono essere centrati orizzontalmente (es. sprite simmetrico).
- Evita di usare "Centra" prima di "Baseline": l'ordine errato produce offset verticali incoerenti tra frame.
- **Pivot X/Y** è relativo alla cella normalizzata, non al singolo frame — impostalo dopo aver definito le dimensioni finali del frame.

---

## 6. Workflow completo suggerito (da immagine grezza a asset esportabile)

```
Apri immagine
   ↓
Sprite Studio – Pulizia
   ├─ Rimozione sfondo (pipeline completa, Tip 1)
   ├─ Defringe
   ├─ Denoise / Isole
   └─ Binarizza alpha
   ↓
Sprite Studio – Slice
   ├─ Rileva sprite automaticamente
   └─ Aggiusta celle manualmente se necessario
   ↓
Animation Studio (se spritesheet animata)
   ├─ Allinea baseline
   ├─ Centra nelle celle
   └─ Imposta pivot
   ↓
Esporta
   ├─ PNG individuale / ZIP
   └─ JSON metadati (compatibile Tiled / motori)
```
