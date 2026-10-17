# Offene Aufgaben

Stand: 2026-10-17. Arbeitsnotiz zum Weitermachen an einem anderen Rechner. Erledigte Punkte löschen; die Datei löschen, wenn nichts mehr offen ist.

## 1. PR #1 – SDK exakt pinnen

- PR: https://github.com/sebfischer83/iryomera-tools/pull/1 (Branch `build/pin-dotnet-sdk`).
- Inhalt: `global.json` auf SDK `10.0.402` mit `rollForward: disable`. Die CI installiert das SDK über `global-json-file`.
- Grund: `dotnet restore --locked-mode` bricht mit jedem anderen SDK ab (NU1004). Das implizite Paket `Microsoft.AspNetCore.App.Internal.Assets` richtet sich nach der SDK-Version. Die Lock-Dateien stammen von 10.0.402 (Runtime 10.0.13).
- Lokal nicht getestet, weil 10.0.402 auf dem alten PC fehlte.
- Zu tun:
  - CI-Ergebnis (Windows und Linux) prüfen und den PR mergen, wenn sie grün ist.
  - Auf dem neuen PC SDK 10.0.402 installieren. Danach lokal `dotnet restore Iryomera.Tools.sln --locked-mode`, Build und Tests laufen lassen.

```bash
winget install --id Microsoft.DotNet.SDK.10 --version 10.0.402 --exact
```

## 2. Branch `feature/phase-5-modio` ansehen

- Der Branch liegt nur auf GitHub und ist nicht in `main` gemergt. Er ist drei Commits voraus:
  - Planung der mod.io-Integration,
  - Implementierung (optionale mod.io-Distribution mit ModKit-Brücke),
  - Phase-5-Verifikationsbericht für Windows und Linux.
- `main`, README und die Bewertung sagen „ModIo nicht begonnen“. Das stimmt nur für `main`.
- Der Game-Session wurde ebenfalls gesagt, Phase 5 sei nicht umgesetzt.
- Zu tun: Stand prüfen, gegen AGENTS.md abgleichen (mod.io optional, nur Mock-HTTP-Tests, keine Live-Requests ohne Konfiguration) und über Merge oder Review entscheiden. Danach die Game-Session bei Bedarf korrigieren.

## 3. Bewusst zurückgestellt

- **Skalierung von ContentStudio:** Jede Änderung validiert das ganze Paket zweimal, baut den Suchindex komplett neu auf und speichert das ganze Paket vorher und nachher im Audit (`src/Iryomera.ContentStudio/Services/ProjectService.cs`). Angehen, bevor echte große Datensätze in Studio kommen. Dafür sind ein ADR und eine Migration nötig.
- **Dichter Codestil:** Nur in Dateien lesbarer machen, die ohnehin geändert werden. Keine reine Umformatierung.

## 4. Kleinigkeiten aus der Bewertung (optional)

- Die Suche in Studio (`Contains` → SQLite `LIKE`) ignoriert Groß-/Kleinschreibung nur für ASCII. Umlaute werden deshalb nicht passend gefunden.
- `ModArchive.PackAsync` lässt `.iryomera-mod.lock` im Projektordner liegen. Das ist dokumentiert, aber vielleicht unerwünscht.
- Die Python-Generatoren schreiben unter Windows CRLF. Inhaltlich gibt es keinen Unterschied, aber der Arbeitsbaum sieht verändert aus. Abhilfe wäre `newline='\n'` beim Schreiben.
- Neu angelegte Datensätze in Studio bekommen `ImportedAt = now`, obwohl nichts importiert wurde.
- Studio hat nur 17 Tests. Es fehlen gezielte Tests für gleichzeitige Änderungen und für den Entzug von Rechten nach einer Übernahme.
