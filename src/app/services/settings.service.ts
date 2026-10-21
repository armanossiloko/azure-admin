import { Injectable, inject } from '@angular/core';
import { AppSettingsDto, SettingsApi, UpdateAppSettingsRequest } from '../api';
import { Branding, normalizeBranding } from './branding.service';

/**
 * Settings as the UI works with them: every field present, Jira keys as a list, branding resolved.
 * The wire shape (`AppSettingsDto`) still carries the legacy `jiraProjectKey` and nullable overrides
 * for older servers and clients; this service is where that compatibility lives.
 */
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

function normalizeProjectKeys(settings: AppSettingsDto): string[] {
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

export function normalizeSettings(settings: AppSettingsDto): AppSettings {
  return {
    conventionalCommitsEnabled: settings.conventionalCommitsEnabled,
    conventionalCommitsUseEmojis: settings.conventionalCommitsUseEmojis,
    excludedGroups: settings.excludedGroups ?? [],
    sprintWeeks: settings.sprintWeeks ?? 0,
    jiraEnabled: settings.jiraEnabled,
    jiraBaseUrl: settings.jiraBaseUrl ?? null,
    jiraProjectKeys: normalizeProjectKeys(settings),
    brandingApplicationName: settings.brandingApplicationName ?? null,
    brandingTagline: settings.brandingTagline ?? null,
    brandingDescription: settings.brandingDescription ?? null,
    branding: normalizeBranding(settings.branding)
  };
}

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly settingsApi = inject(SettingsApi);

  async getSettings(): Promise<AppSettings> {
    return normalizeSettings(await this.settingsApi.settingsGet());
  }

  async updateSettings(settings: AppSettings): Promise<AppSettings> {
    // Send both shapes so a still-running older API that only knows jiraProjectKey keeps working.
    const body: UpdateAppSettingsRequest = {
      conventionalCommitsEnabled: settings.conventionalCommitsEnabled,
      conventionalCommitsUseEmojis: settings.conventionalCommitsUseEmojis,
      excludedGroups: settings.excludedGroups,
      sprintWeeks: settings.sprintWeeks,
      jiraEnabled: settings.jiraEnabled,
      jiraBaseUrl: settings.jiraBaseUrl,
      jiraProjectKeys: settings.jiraProjectKeys,
      jiraProjectKey: settings.jiraProjectKeys.length ? settings.jiraProjectKeys.join(',') : null,
      brandingApplicationName: settings.brandingApplicationName,
      brandingTagline: settings.brandingTagline,
      brandingDescription: settings.brandingDescription
    };
    return normalizeSettings(await this.settingsApi.settingsUpdate({ body }));
  }
}
