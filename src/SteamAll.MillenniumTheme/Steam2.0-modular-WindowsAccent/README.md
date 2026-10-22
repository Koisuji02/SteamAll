# Steam 2.0 Modular (Windows Accent)

Copia locale di Steam 2.0 Modular con i gradienti legati al colore principale di Windows.

- Gradiente: ogni finestra ha un unico gradiente che sale dal basso. Parte dal tono della
  barra delle applicazioni: Windows la tinge con Dark1, ma con la trasparenza attiva si vede
  tra Dark1 e Dark2 a seconda dello sfondo, quindi parte a metà tra i due. Salendo il colore
  si scurisce gradualmente (curva esponenziale, interpolazione OKLab) fino a Dark3 mescolato
  con un po' di nero: colorato in basso, più scuro in alto.
  I pannelli usano `background-attachment: fixed`, quindi mostrano tutti una parte dello
  stesso gradiente. Le zone che scorrono (lista della libreria, pagina di dettaglio) no: Steam
  dà a quei contenitori una `transform`, che "aggancia" a sé gli sfondi fixed e il gradiente
  scorrerebbe col contenuto. Lì il gradiente è dipinto sul contenitore che scorre (che resta
  fermo), alto come la finestra e allineato a essa (`--s2m-scroller-background`), e il
  contenuto sopra è trasparente: scorrendo il gradiente resta sempre lo stesso. Tavolozza `--s2m-*` in cima a `libraryroot.custom.css`: per cambiare il
  colore di partenza basta modificare `--s2m-bottom` (per esempio
  `var(--SystemAccentColorDark1)` se disattivi la trasparenza di Windows).
- Pagina di dettaglio di un gioco: l'artwork occupa tutta la finestra e la barra Gioca/Aggiorna
  sta in fondo, sopra il footer di Steam (altezza = area visibile della pagina, unità `cqh`,
  meno la barra; `--s2m-header-height`, `--s2m-footer-height`, `--s2m-playbar-height` in cima
  al CSS fanno da rete di sicurezza). Steam su desktop limita l'artwork a 320px: il limite è
  rimosso. Vale anche per i giochi non Steam (BoilR, artwork personalizzato): Steam dà loro la
  classe "FallbackArt" con un'altezza fissa di 24vw, sostituita dalla stessa altezza a tutta
  finestra; resta quella di Steam solo se il gioco non ha nessuna immagine. Sfondo, barra dei pulsanti (anche quella fissa che compare scorrendo), riga dei
  link, riquadro attività, "In evidenza" e "Amici che giocano" seguono il tema invece del
  grigio di Steam; il pulsante Gioca/Aggiorna/Installa usa il colore principale.
  Usa i nomi delle classi di Steam (build di settembre 2026), versione desktop (le regole
  `.BasicUI` di Steam sono per l'interfaccia in stile gamepad): un aggiornamento di Steam può
  cambiarli e queste regole smetterebbero di applicarsi.
- Artwork in alta risoluzione per i giochi Steam: Steam mostra il suo `library_hero.jpg`, che
  scarica a 1920x620 (a tutta finestra si vede sgranato), anche quando il suo CDN ha
  `library_hero_2x.jpg` a 3840x1240. Quando la pagina mostra l'artwork ufficiale, il tema carica
  la versione 2x e la usa se esiste (`libraryroot.custom.js`, "HD ARTWORK FOR STEAM GAMES").
  Le immagini personalizzate (BoilR, plugin SteamGridDB, "Imposta sfondo personalizzato") hanno
  sempre la precedenza e non vengono toccate.
- Giochi degli store da installare: il tasto Installa (giochi grigi, fuori dagli installati) non è più
  nel tema: lo dà il plugin di SteamAll per Millennium (steamall-install-button), che funziona con
  qualsiasi tema e con tutti gli store di SteamAll.
- Pulsanti: usano il colore principale; se è troppo chiaro per il testo bianco viene scurito
  mantenendo la tonalità.
- Aggiornamento automatico: ogni secondo (solo per le finestre visibili, più subito quando una
  finestra torna visibile o in primo piano) `libraryroot.custom.js` chiede a Millennium il
  colore attuale (`Core_GetSystemColors`) e aggiorna le variabili: con la presentazione di
  sfondi i gradienti seguono il nuovo colore entro circa un secondo.
- Finestra "Customize Colors" del tema: i colori con la **W** seguono Windows; scegliendo un
  colore a mano diventa fisso; **Reset** torna ai colori di Windows. Le impostazioni sono
  salvate a parte (`steam_color_config_winaccent_v3`), separate da quelle del tema originale.
- Senza blocco `github` in `skin.json`: Millennium non la aggiorna sovrascrivendo le modifiche.

---

# Steam 2.0 Modular

![Steam 2.0 Modular Header](https://i.imgur.com/sCCMnkG.png)

## Description

Steam 2.0 Modular is an elegant and minimalist theme inspired by GOG 2.0 that modernizes the Steam experience. This theme focuses on simplifying the interface by removing unnecessary visual elements and reorganizing content more efficiently, all while maintaining the full functionality of Steam.

![Steam 2.0 Modular Header](https://i.imgur.com/4SJRBxy.png)
![Steam 2.0 Modular Header](https://i.imgur.com/4DKpfjz.png)



