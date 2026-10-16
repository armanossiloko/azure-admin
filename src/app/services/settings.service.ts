import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map, Observable } from 'rxjs';
import { Branding, DEFAULT_BRANDING } from './branding.service';

export type AppSettings = {
  conventionalCommitsEnabled: boolean;
  conventionalCommitsUseEmojis: boolean;
  excludedGroups: string[];
  sprintWeeks: number;
  jiraEnabled: boolean;
  jiraBaseUrl: string | null;
  jiraProjectKeys: string[];
  /**
   * Stored overrides. On GET: null when unset. On PUT: send "" to clear an override
   * (null would mean leave unchanged for older clients that omit the fields).
   */
  brandingApplicationName: string | null;
  brandingTagline: string | null;
  brandingDescription: string | null;
  branding: Branding;
};

/** Raw API payload; includes legacy `jiraProjectKey` for older backends / round-trips. */
type AppSettingsResponse = Omit<AppSettings, 'jiraProjectKeys' | 'branding'> & {
  jiraProjectKeys?: string[] | null;
  jiraProjectKey?: string | null;
  brandingApplicationName?: string | null;
  brandingTagline?: string | null;
  brandingDescription?: string | null;
  branding?: Partial<Branding> | null;
};

function normalizeProjectKeys(settings: AppSettingsResponse): string[] {
  if (settings.jiraProjectKeys?.length) {
    return settings.jiraProjectKeys.map((k) => k.trim().toUpperCase()).filter(Boolean);
  }

  // Existing installs stored a single key in jiraProjectKey (no commas).
  if (settings.jiraProjectKey?.trim()) {
    return settings.jiraProjectKey
      .split(',')
      .map((k) => k.trim().toUpperCase())
      .filter(Boolean);
  }

  return [];
}

function normalizeBranding(settings: AppSettingsResponse): Branding {
  const b = settings.branding;
  return {
    applicationName: b?.applicationName?.trim() || DEFAULT_BRANDING.applicationName,
    tagline: b?.tagline?.trim() || DEFAULT_BRANDING.tagline,
    description: b?.description?.trim() || DEFAULT_BRANDING.description
  };
}

function normalizeSettings(settings: AppSettingsResponse): AppSettings {
  return {
    conventionalCommitsEnabled: settings.conventionalCommitsEnabled,
    conventionalCommitsUseEmojis: settings.conventionalCommitsUseEmojis,
    excludedGroups: settings.excludedGroups ?? [],
    sprintWeeks: settings.sprintWeeks ?? 0,
    jiraEnabled: settings.jiraEnabled,
    jiraBaseUrl: settings.jiraBaseUrl,
    jiraProjectKeys: normalizeProjectKeys(settings),
    brandingApplicationName: settings.brandingApplicationName ?? null,
    brandingTagline: settings.brandingTagline ?? null,
    brandingDescription: settings.brandingDescription ?? null,
    branding: normalizeBranding(settings)
  };
}

@Injectable({ providedIn: 'root' })
export class SettingsService {
  constructor(private readonly http: HttpClient) {}

  getSettings(): Observable<AppSettings> {
    return this.http.get<AppSettingsResponse>('/api/settings').pipe(map(normalizeSettings));
  }

  updateSettings(settings: AppSettings): Observable<AppSettings> {
    // Send both shapes so a still-running older API that only knows jiraProjectKey keeps working.
    const body: AppSettingsResponse = {
      ...settings,
      jiraProjectKeys: settings.jiraProjectKeys,
      jiraProjectKey: settings.jiraProjectKeys.length ? settings.jiraProjectKeys.join(',') : null,
      brandingApplicationName: settings.brandingApplicationName,
      brandingTagline: settings.brandingTagline,
      brandingDescription: settings.brandingDescription
    };
    return this.http.put<AppSettingsResponse>('/api/settings', body).pipe(map(normalizeSettings));
  }
}
