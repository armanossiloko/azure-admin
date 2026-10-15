import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map, Observable } from 'rxjs';

export type AppSettings = {
  conventionalCommitsEnabled: boolean;
  conventionalCommitsUseEmojis: boolean;
  excludedGroups: string[];
  sprintWeeks: number;
  jiraEnabled: boolean;
  jiraBaseUrl: string | null;
  jiraProjectKeys: string[];
};

/** Raw API payload; includes legacy `jiraProjectKey` for older backends / round-trips. */
type AppSettingsResponse = Omit<AppSettings, 'jiraProjectKeys'> & {
  jiraProjectKeys?: string[] | null;
  jiraProjectKey?: string | null;
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

function normalizeSettings(settings: AppSettingsResponse): AppSettings {
  return {
    conventionalCommitsEnabled: settings.conventionalCommitsEnabled,
    conventionalCommitsUseEmojis: settings.conventionalCommitsUseEmojis,
    excludedGroups: settings.excludedGroups ?? [],
    sprintWeeks: settings.sprintWeeks ?? 0,
    jiraEnabled: settings.jiraEnabled,
    jiraBaseUrl: settings.jiraBaseUrl,
    jiraProjectKeys: normalizeProjectKeys(settings)
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
      jiraProjectKey: settings.jiraProjectKeys.length ? settings.jiraProjectKeys.join(',') : null
    };
    return this.http.put<AppSettingsResponse>('/api/settings', body).pipe(map(normalizeSettings));
  }
}
