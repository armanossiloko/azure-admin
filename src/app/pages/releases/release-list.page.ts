import { CommonModule } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SelectedOrgService } from '../../services/selected-org.service';

type ReleaseSummary = {
  id: string;
  title: string;
  sprintLabel: string | null;
  status: string;
  createdAt: string;
};

@Component({
  standalone: true,
  selector: 'app-release-list-page',
  imports: [CommonModule, RouterLink, FormsModule],
  templateUrl: './release-list.page.html'
})
export class ReleaseListPage {
  private readonly selectedOrg = inject(SelectedOrgService);

  protected readonly rows = signal<ReleaseSummary[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly statusFilter = signal('');

  constructor(private readonly http: HttpClient) {
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
      const params: Record<string, string> = {};
      const orgId = this.selectedOrg.selectedOrgId();
      if (orgId) params['organizationId'] = orgId;
      const status = this.statusFilter().trim();
      if (status) params['status'] = status;

      const list = await firstValueFrom(
        this.http.get<ReleaseSummary[]>('/api/releases', { params })
      );
      this.rows.set(list ?? []);
    } catch {
      this.error.set('Failed to load releases.');
    } finally {
      this.loading.set(false);
    }
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
      await firstValueFrom(this.http.delete(`/api/releases/${id}`));
      this.rows.update(rows => rows.filter(r => r.id !== id));
    } catch {
      this.error.set('Failed to delete release.');
    }
  }
}
