import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

export type ThemeId = 'dark' | 'dark-blue' | 'light';

export type ThemeOption = {
  id: ThemeId;
  label: string;
  /** CSS color for the swatch preview in menus */
  swatch: string;
};

export const THEME_OPTIONS: readonly ThemeOption[] = [
  { id: 'dark', label: 'Dark', swatch: '#2e2c2a' },
  { id: 'dark-blue', label: 'Dark blue', swatch: '#1a2744' },
  { id: 'light', label: 'Light', swatch: '#f4f2ef' }
] as const;

const STORAGE_KEY = 'azure-admin-theme';
const THEME_IDS = new Set<ThemeId>(THEME_OPTIONS.map(o => o.id));

export function parseThemeId(value: string | null | undefined): ThemeId | null {
  if (!value) return null;
  return THEME_IDS.has(value as ThemeId) ? (value as ThemeId) : null;
}

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly doc = inject(DOCUMENT);

  readonly theme = signal<ThemeId>('dark');
  readonly options = THEME_OPTIONS;

  constructor() {
    const initial = this.readStored();
    this.applyDom(initial);
    this.theme.set(initial);
  }

  setTheme(theme: ThemeId): void {
    this.applyDom(theme);
    this.theme.set(theme);
    try {
      localStorage.setItem(STORAGE_KEY, theme);
    } catch {
      /* ignore quota / private mode */
    }
  }

  labelFor(theme: ThemeId = this.theme()): string {
    return THEME_OPTIONS.find(o => o.id === theme)?.label ?? 'Dark';
  }

  private readStored(): ThemeId {
    try {
      return parseThemeId(localStorage.getItem(STORAGE_KEY)) ?? 'dark';
    } catch {
      return 'dark';
    }
  }

  private applyDom(theme: ThemeId): void {
    this.doc.documentElement.setAttribute('data-theme', theme);
  }
}
