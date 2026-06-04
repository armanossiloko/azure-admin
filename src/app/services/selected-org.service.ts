import { Injectable, signal } from '@angular/core';

const SelectedOrgStorageKey = 'azure-admin.selected-org-id';

@Injectable({ providedIn: 'root' })
export class SelectedOrgService {
  readonly selectedOrgId = signal<string | null>(null);

  readStored(): string | null {
    try {
      return globalThis.sessionStorage?.getItem(SelectedOrgStorageKey) ?? null;
    } catch {
      return null;
    }
  }

  select(id: string | null): void {
    this.selectedOrgId.set(id);
    try {
      if (id) globalThis.sessionStorage?.setItem(SelectedOrgStorageKey, id);
      else globalThis.sessionStorage?.removeItem(SelectedOrgStorageKey);
    } catch {
      /* ignore */
    }
  }

  organizationQueryParam(): string | null {
    const id = this.selectedOrgId();
    return id ? `organizationId=${encodeURIComponent(id)}` : null;
  }
}
