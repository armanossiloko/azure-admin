import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

export type ThemeId = 'light' | 'dark';

const STORAGE_KEY = 'azure-admin-theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly doc = inject(DOCUMENT);

  readonly theme = signal<ThemeId>('dark');

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

  private readStored(): ThemeId {
    try {
      return localStorage.getItem(STORAGE_KEY) === 'light' ? 'light' : 'dark';
    } catch {
      return 'dark';
    }
  }

  private applyDom(theme: ThemeId): void {
    this.doc.documentElement.setAttribute('data-theme', theme);
  }
}
