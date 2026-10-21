import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';
import { BrandingApi, BrandingDto } from '../api';

export type Branding = BrandingDto;

/** Built-in defaults — keep in sync with backend BrandingDefaults. */
export const DEFAULT_BRANDING: Branding = {
  applicationName: 'Azure Admin',
  tagline: 'Release control',
  description:
    'Release control for Azure DevOps — teams, repositories, and promotion pull requests in one place.'
};

@Injectable({ providedIn: 'root' })
export class BrandingService {
  private readonly brandingApi = inject(BrandingApi);
  private readonly doc = inject(DOCUMENT);

  readonly branding = signal<Branding>({ ...DEFAULT_BRANDING });
  readonly loaded = signal(false);

  private loadPromise: Promise<Branding> | null = null;

  /** Idempotent; safe to call from App + login/shell. */
  load(): Promise<Branding> {
    if (!this.loadPromise) {
      this.loadPromise = this.fetchAndApply().finally(() => this.loaded.set(true));
    }
    return this.loadPromise;
  }

  /** Apply resolved branding after settings save (skips re-fetch race). */
  apply(branding: Branding): void {
    const next = normalizeBranding(branding);
    this.branding.set(next);
    this.applyDocument(next);
    this.loadPromise = Promise.resolve(next);
    this.loaded.set(true);
  }

  async reload(): Promise<Branding> {
    this.loadPromise = null;
    return this.load();
  }

  private async fetchAndApply(): Promise<Branding> {
    try {
      const remote = await this.brandingApi.brandingGet();
      const next = normalizeBranding(remote);
      this.branding.set(next);
      this.applyDocument(next);
      return next;
    } catch {
      const fallback = { ...DEFAULT_BRANDING };
      this.branding.set(fallback);
      this.applyDocument(fallback);
      return fallback;
    }
  }

  private applyDocument(b: Branding): void {
    this.doc.title = b.applicationName;
    const metaDesc = this.doc.querySelector('meta[name="description"]');
    if (metaDesc) metaDesc.setAttribute('content', b.description);
    const appName = this.doc.querySelector('meta[name="application-name"]');
    if (appName) appName.setAttribute('content', b.applicationName);
    const appleTitle = this.doc.querySelector('meta[name="apple-mobile-web-app-title"]');
    if (appleTitle) appleTitle.setAttribute('content', b.applicationName);
  }
}

export function normalizeBranding(value: Partial<Branding> | null | undefined): Branding {
  return {
    applicationName: value?.applicationName?.trim() || DEFAULT_BRANDING.applicationName,
    tagline: value?.tagline?.trim() || DEFAULT_BRANDING.tagline,
    description: value?.description?.trim() || DEFAULT_BRANDING.description
  };
}
