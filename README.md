# ARZsoftware FHIR Profile Comparer

Dieses Repository enthält den **FHIR Profile Comparer**, ein Tool zum automatisierten Abgleich von FHIR-Strukturdefinitionen aus Simplifier-Paketen (z.B. Gematik E-Rezept Releases). Das Tool ermöglicht es, Änderungen (hinzugefügte, entfernte oder modifizierte Elemente und Slices) zwischen zwei Versionen eines FHIR-Pakets auf einen Blick zu analysieren.

## Architektur

Das Projekt ist ein eigenständiger Microservice und besteht aus zwei Hauptkomponenten:

- **`src/Backend/ArzSw.FhirProfileComparer.Api`**
  Eine leichtgewichtige ASP.NET Core Minimal API (.NET 8). Sie nutzt das offizielle Firely .NET SDK (`Hl7.Fhir.R4`), um Pakete live von `packages.simplifier.net` herunterzuladen, zu entpacken und die `StructureDefinition`-Ressourcen im Arbeitsspeicher zu vergleichen. 
  *(Dieses Backend benötigt **keine** Datenbankverbindung).*

- **`src/Frontend`**
  Eine Angular-Anwendung (Single Page Application). Sie visualisiert die Ergebnisse des Backends übersichtlich in Tabellen und färbt geänderte Elemente zur schnellen Erkennung ein.

## Lokale Entwicklung & Starten

Um das Projekt lokal auszuführen, müssen Backend und Frontend gestartet werden.

### 1. Backend starten
Das Backend läuft standardmäßig auf Port `5038` (HTTP) bzw. `7167` (HTTPS).

```bash
cd src/Backend/ArzSw.FhirProfileComparer.Api
dotnet run
```

Die Swagger-Dokumentation der API ist dann unter `http://localhost:5038/swagger` erreichbar.

### 2. Frontend starten
Das Angular-Frontend benötigt Node.js. Es ist so konfiguriert (`proxy.conf.json`), dass es API-Anfragen automatisch an `http://localhost:5038` weiterleitet (CORS-freundlich).

```bash
cd src/Frontend
npm install
npm start
```

Das Frontend ist danach unter `http://localhost:4200` im Browser erreichbar.

## Changelog
Informationen zu den letzten Updates und Fehlerbehebungen (z.B. Slice-Deduplizierung) befinden sich in der [CHANGELOG.md](CHANGELOG.md).
