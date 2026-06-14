import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export interface ElementDelta {
  path: string;
  property: string;
  sliceName: string;
  oldValue: string;
  newValue: string;
  type: string;
}

export interface ProfileDelta {
  profileUrl: string;
  profileName: string;
  profilePaths: string[];
  isNew: boolean;
  isRemoved: boolean;
  addedElements: ElementDelta[];
  removedElements: ElementDelta[];
  modifiedElements: ElementDelta[];
}

export interface CompareResult {
  packageId: string;
  sourceVersion: string;
  targetVersion: string;
  targetChangelog: string;
  profiles: ProfileDelta[];
}

import { marked } from 'marked';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  private http = inject(HttpClient);
  public appVersion = '1.4.1';
  public appChangelog = [
    'v1.4.1: Bugfix: Doppelte Felder bei Slices (z.B. value[x] / extension) werden nun sauber dedupliziert',
    'v1.4.0: eAbrechnungsdaten auf GKV-SV Paket korrigiert (de.gkvsv.erezeptabrechnungsdaten)',
    'v1.4.0: Profil-Pfade nun auch für neu eingeführte Profile sichtbar',
    'v1.4.0: UI Layout für Deltas komplett auf Tabellen umgestellt (bessere Lesbarkeit)',
    'v1.3.0: UI Umbau (Spalten getauscht, alle Bereiche klappbar, mehrere Pfade)',
    'v1.3.0: Release-Notes aller Zwischenversionen, Quittung-Filter (GEM_ERP_PR_Bundle), ignorieren von reinen Versions-Änderungen in Constraints',
    'v1.2.1: Bugfixes für max=0 (verbotene Felder) und Slice-Matching (ElementId)',
    'v1.2.0: Dependency Graph für Profil-Pfade (z.B. Bundle -> Prescription -> Narcotic)',
    'v1.1.0: FHIR Slice-Namen (z.B. PZN) hinzugefügt',
    'v1.1.0: Simplifier Release Notes (Markdown) integriert',
    'v1.1.0: Spezifische API-Fehlermeldungen im UI eingebaut',
    'v1.1.0: UI optimiert (Akkordeons, Copy-URL, lange URLs gekürzt, Default-Werte für Min-Cardinality gefixt)'
  ];

  packages = [
    { id: 'kbv.ita.erp', name: 'eVerordnung (KBV)' },
    { id: 'de.abda.erezeptabgabedaten', name: 'eAbgabedaten (ABDA)' },
    { id: 'de.gematik.erezept-workflow.r4', name: 'Quittung komplett (gematik)' },
    { id: 'de.gematik.erezept-workflow.r4-bundle', name: 'Quittung nur ab GEM_ERP_PR_Bundle (gematik)' },
    { id: 'de.gkvsv.erezeptabrechnungsdaten', name: 'eAbrechnungsdaten (GKV-SV)' }
  ];

  selectedPackage = this.packages[0].id;
  sourceVersion = '1.1.0';
  targetVersion = '1.4.2';

  isLoading = signal(false);
  error = signal<string | null>(null);
  result = signal<CompareResult | null>(null);

  compare() {
    this.isLoading.set(true);
    this.error.set(null);
    this.result.set(null);

    // The API call uses a relative path. In dev mode, proxy.conf.json routes this to localhost:5030.
    // In production, the Docker nginx container proxies this to the backend 'api' service.
    const apiPackageId = this.selectedPackage.replace('-bundle', '');
    const url = `/api/fhir/compare?packageId=${apiPackageId}&sourceVersion=${this.sourceVersion}&targetVersion=${this.targetVersion}`;

    this.http.get<CompareResult>(url).subscribe({
      next: (res) => {
        if (this.selectedPackage.endsWith('-bundle')) {
          res.profiles = res.profiles.filter(p => p.profilePaths && p.profilePaths.some(path => path.includes('GEM_ERP_PR_Bundle')));
        }
        
        for (const profile of res.profiles) {
          profile.addedElements = this.deduplicateElements(profile.addedElements);
          profile.removedElements = this.deduplicateElements(profile.removedElements);
        }

        this.result.set(res);
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error(err);
        if (err.error && err.error.error) {
          this.error.set(err.error.error);
        } else {
          this.error.set('Fehler beim Vergleichen der Profile. Ist das Backend gestartet?');
        }
        this.isLoading.set(false);
      }
    });
  }

  private deduplicateElements(elements: ElementDelta[]): ElementDelta[] {
    const map = new Map<string, ElementDelta[]>();
    for (const el of elements) {
      if (!map.has(el.path)) map.set(el.path, []);
      map.get(el.path)!.push(el);
    }
    
    const result: ElementDelta[] = [];
    for (const els of map.values()) {
      if (els.length === 1) {
        result.push(els[0]);
      } else {
        const withSlice = els.filter(e => e.sliceName && e.sliceName.trim() !== '');
        if (withSlice.length > 0) {
          result.push(...withSlice);
        } else {
          result.push(els[0]);
        }
      }
    }
    return elements.filter(el => result.includes(el));
  }

  getRenderedChangelog(): string {
    const cl = this.result()?.targetChangelog;
    if (!cl) return '';
    return marked.parse(cl) as string;
  }

  copyUrl(event: MouseEvent, url: string) {
    event.stopPropagation(); // Prevent accordion from toggling
    navigator.clipboard.writeText(url).then(() => {
      // Optional: show a quick tooltip or toast here
    });
  }
}
