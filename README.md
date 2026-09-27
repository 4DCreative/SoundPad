# SoundPad

Soundboard für Windows 11 mit WPF / .NET 10, Visual Studio 2026 und NAudio 2.2.1.

## Starten und bedienen
SoundPad.sln öffnen, SoundPad als Startprojekt verwenden und F5 drücken.
Alternativ: dotnet run --project src/SoundPad

- „Sound hinzufügen“ importiert eine oder mehrere WAV-/MP3-Dateien.
- Antippen startet, erneutes Antippen stoppt; der nächste Start beginnt von vorn.
- Jedes Pad zeigt seine Gesamtdauer. Während der Wiedergabe erscheint die aktuelle Position als „Position / Gesamtdauer“; bei einer Schleife beginnt die Positionsanzeige nach jedem Durchlauf wieder bei 0:00.
- Das zuletzt gestartete Pad bleibt nach Ende der Wiedergabe als „Zuletzt gespielt“ sichtbar und wird gedimmt dargestellt.
- Mehrere Sounds dürfen gleichzeitig laufen. „Alle stoppen“ beendet auch vorgemerkte Starts während des Ladens.
- Der Umschalter unten legt pro Set fest, ob ein neues Pad zusätzlich startet oder zuvor alle anderen laufenden Pads stoppt.
- Das Zahnrad öffnet Name, Farbe, Einmal-/Schleifenmodus und Lautstärke. Änderungen gelten nach „Speichern“, auch für laufende Sounds.
- „Pad entfernen“ entfernt nur die Zuordnung, niemals die Audiodatei.
- Links sitzt ein vertikaler Gesamtlautstärkeregler mit Prozentanzeige und großem Griff.
- Das Auswahlfeld oben rechts markiert ein Pad, ohne es abzuspielen. „Alle auswählen“ / „Auswahl aufheben“ steuert die gesamte Auswahl. „Auf Auswahl übertragen“ speichert den Gesamtpegel als Startlautstärke der markierten Pads; relative Pad-Pegel und Wiedergabestatus bleiben erhalten. Die Auswahl selbst wird nicht gespeichert.
- In den Pad-Einstellungen legt „Startlautstärke“ fest, auf welchen Gesamtpegel der Regler beim tatsächlichen Start dieses Sounds gesetzt wird. Das betrifft alle laufenden Sounds. Manuelles Nachregeln bleibt bis zum nächsten Pad-Start erhalten; Stoppen verändert den Gesamtpegel nicht.
- „Pad-Lautstärke“ bleibt ein separater relativer Pegel. Änderungen der Startlautstärke greifen erst beim nächsten Start, nicht beim Speichern des Dialogs. Neue Pads übernehmen als Startwert die aktuelle Gesamtlautstärke; bestehende Belegungen ohne diesen Wert erhalten 80 Prozent.
- Vollbild (Escape beendet Vollbild) und Logordner sind direkt erreichbar.
- Das quadratische Raster passt sich an die Fläche an. Bei vielen Pads werden sie auf mehrere Seiten verteilt; „Zurück“ und „Weiter“ wechseln die Seite. Es gibt keinen Scrollbalken im Pad-Bereich.
- Unten rechts wechselt „Listenansicht“ zwischen großen quadratischen Pads und einer kompakten Zeilenansicht. Die Auswahl wird pro Set gespeichert.

## Belegung
Beim ersten Start ist kein Set geöffnet; die Ansicht ist leer. „Neues Set“ legt nach Wahl eines Namens eine leere `.soundpadset`-Datei an. Mit „Set speichern unter…“ wird die aktuelle Belegung als Datei gespeichert; „Set öffnen“ wechselt zu einer vorhandenen Datei. Nach dem Öffnen oder Anlegen werden alle Änderungen automatisch in dieses aktive Set gespeichert. Beim nächsten Start öffnet SoundPad dieses zuletzt aktive Set wieder. Falls die Datei nicht mehr vorhanden ist oder die Markierung nicht gelesen werden kann, startet SoundPad wieder mit einer leeren Ansicht. Vor dem Wechsel werden laufende Sounds gestoppt. Jede Speicherung legt die vorherige Fassung als `.bak` ab. Die Sounddateien werden referenziert, nicht kopiert. Nach Verschieben einer Datei das Pad entfernen und neu hinzufügen. Defekte Belegungen werden nicht überschrieben: Die Anwendung zeigt den Fehler und deaktiviert für diese Sitzung das Speichern.

## Audio
Dateien werden abschnittsweise im Hintergrund gelesen und in 48 kHz Stereo umgerechnet. Ein gemeinsamer Mixer gibt alle Pads über das Windows-Standardausgabegerät aus (WASAPI Shared, Multimedia-Standardgerät, angeforderte Pufferlatenz 80 ms). Diese Pufferangabe ist keine gemessene Ende-zu-Ende-Latenz. Beim Gerätefehler werden Pads gestoppt; nach Korrektur der Windows-Ausgabe erneut starten.

Die frühere 175-Sekunden-Grenze ist aufgehoben. Auch drei- bis sechsminütige Stücke werden gestreamt. Pro aktivem Pad hält die Anwendung maximal ca. 576 KiB an Sample-Blöcken; Decoder und Windows-Ausgabe benötigen zusätzlichen Speicher. Der Import prüft den Anfang der Datei, die restliche Datei wird während der Wiedergabe gelesen. Beschädigte spätere Stellen können daher erst beim Abspielen auffallen. Die Datei muss während der Wiedergabe erreichbar bleiben. Bei zu langsamen Datenträgern liefert der Mixer vorübergehend Stille statt den Audiothread zu blockieren; diese Pufferunterläufe werden beim Stoppen/Ende protokolliert. Jeder neue Start öffnet die Datei erneut.

Schleifen wiederholen die ganze Datei. Die Datei muss für einen sauberen Übergang passend geschnitten sein; diese Version enthält weder Crossfade noch BPM-Synchronisierung oder Time-Stretching. Die Ausgabe begrenzt übersteuerte Samples auf den gültigen Bereich; bei Clipping bitte Lautstärken reduzieren.

## Logging
JSONL-Dateien unter logs neben SoundPad.sln. Ohne Projektmappe: neben der Anwendung. Bei fehlenden Schreibrechten: %LOCALAPPDATA%/SoundPad/logs. Die Schaltfläche „Logs“ öffnet den tatsächlichen Ordner; Schreibfehler erscheinen in der Statuszeile.

Enthalten sind Zeitstempel mit Zeitzone, Schweregrad, Ereignis, Sitzung, Prozess-/Thread-ID, Details und bei Fehlern Stacktraces. Dateien pro Tag/Sitzung, weitere Segmente ab 10 MiB. Keine automatische Löschung. Alte Logs können bei geschlossener Anwendung entfernt werden. Logs sind von Git ausgeschlossen.

Ereignisse: Start/Ende, Fensteraufbau, Import und Streaming-Vorbereitungsdauer, Start/Stop/Ende eines Pads, Einstellungen, Speicherung, Audioausgabe und Fehler. Pads.StartVolumeTransferred protokolliert die geänderten Pad-IDs mit altem und neuem Startpegel. Audio.StreamUnderruns / Pad.Completed enthalten gegebenenfalls Pufferunterläufe; Pad.StreamFailed erfasst Lesefehler während der Wiedergabe. Audio.StartVolumeApplied protokolliert Pad-ID, vorherigen Gesamtpegel und neuen Startpegel. Audio.OutputOpened enthält Backend, ausgewähltes Gerät, Geräte-ID, Windows-Mixformat und Streamformat. Audio.Clipping zählt übersteuerte Samples in etwa fünf Sekunden. preparationMs misst Vorbereitung bis zur Übergabe an den Mixer, nicht die hörbare Verzögerung. Dateinamen und Ausnahmeinformationen können lokale Pfade enthalten.

Keine Dateizugriffe oder Logs im Audio-Callback. Die synchrone Logausgabe außerhalb davon ist für seltene Anwendungsereignisse gedacht. Fatale Fehler werden protokolliert, nicht unterdrückt; harte Prozessabbrüche lassen sich nicht zuverlässig erfassen.

## Prüfung
- dotnet build SoundPad.sln
- dotnet run --project tests/SoundPad.Checks
- Optional: dotnet run --project tests/SoundPad.Checks -- --render --device

Die eigenständigen Prüfungen decken Mixer, Schleifen, Stop/Neustart, Pegel, Speicherung und WAV-Konvertierung ab. --device öffnet die Windows-Audioausgabe mit Stille. --render erzeugt unter artifacts eine Vorschau echter WPF-Steuerelemente mit Beispielbelegung, ohne diese zu speichern. Die Beispiel-Pads sind keine mitgelieferten Sounds.

Noch am Gerät prüfen: hörbare WAV-/MP3-Wiedergabe, Touchbedienung, Gerätesteckerwechsel und subjektive Reaktionszeit.




