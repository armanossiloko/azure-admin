import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export type AppSettings = {
  conventionalCommitsEnabled: boolean;
  conventionalCommitsUseEmojis: boolean;
  excludedGroups: string[];
  jiraEnabled: boolean;
  jiraBaseUrl: string | null;
  jiraProjectKey: string | null;
};

@Injectable({ providedIn: 'root' })
export class SettingsService {
  constructor(private readonly http: HttpClient) {}

  getSettings(): Observable<AppSettings> {
    return this.http.get<AppSettings>('/api/settings');
  }

  updateSettings(settings: AppSettings): Observable<AppSettings> {
    return this.http.put<AppSettings>('/api/settings', settings);
  }
}
