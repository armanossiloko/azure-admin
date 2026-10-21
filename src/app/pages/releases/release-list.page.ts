import { CommonModule } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { RELEASE_LIFECYCLE_STATUS, ReleaseLifecycleStatus, ReleasesApi, ReleaseSummaryDto } from '../../api';
import { SelectedOrgService } from '../../services/selected-org.service';

@Component({
  standalone: true,
  selector: 'app-release-list-page',
  imports: [CommonModule, RouterLink, FormsModule],
  templateUrl: './release-list.page.html'
})
export class ReleaseListPage {
  private readonly selectedOrg = inject(SelectedOrgService);
  private readonly releasesApi = inject(ReleasesApi);

  protected readonly rows = signal<ReleaseSummaryDto[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly statusFilter = signal('');

  constructor() {
    effect(() => {
      this.selectedOrg.selectedOrgId();
      this.statusFilter();
      void this.load();
    });
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const list = await this.releasesApi.releasesList({
        organizationId: this.selectedOrg.selectedOrgId() ?? undefined,
        status: this.parseStatus(this.statusFilter())
      });
      this.rows.set(list ?? []);
    } catch {
      this.error.set('Failed to load releases.');
    } finally {
      this.loading.set(false);
    }
  }

  private parseStatus(value: string): ReleaseLifecycleStatus | undefined {
    const wanted = value.trim().toLowerCase();
    return RELEASE_LIFECYCLE_STATUS.find(s => s.toLowerCase() === wanted);
  }

  protected statusBadgeClass(status: string): string {
    switch ((status || '').toLowerCase()) {
      case 'active':
        return 'badge active';
      case 'completed':
        return 'badge ok';
      case 'archived':
        return 'badge';
      default:
        return 'badge draft';
    }
  }

  protected isReleaseClosed(status: string): boolean {
    const s = (status || '').toLowerCase();
    return s === 'completed' || s === 'archived';
  }

  protected async delete(id: string, title: string): Promise<void> {
    if (!confirm(`Delete release "${title}"? This will remove all associated PRs and data.`)) return;
    try {
      await this.releasesApi.releasesDelete({ id });
      this.rows.update(rows => rows.filter(r => r.id !== id));
    } catch {
      this.error.set('Failed to delete release.');
    }
  }
}
