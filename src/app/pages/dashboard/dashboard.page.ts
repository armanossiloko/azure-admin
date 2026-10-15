import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../auth/auth.service';
import { SelectedOrgService } from '../../services/selected-org.service';

type DashboardStats = {
  activeReleasesCount: number;
  openPullRequestsCount: number;
  pullRequestsNeedingAttentionCount: number;
  registeredRepositoriesCount: number;
  distinctAzureDevOpsProjectsCount: number;
};

type DashboardChecklist = {
  hasAzureOrganization: boolean;
  hasTeam: boolean;
  hasRegisteredRepository: boolean;
  hasRelease: boolean;
};

type DashboardActivityItem = {
  kind: string;
  title: string;
  subtitle: string | null;
  occurredAt: string;
  href: string | null;
};

type DashboardReleaseSummary = {
  id: string;
  title: string;
  sprintLabel: string | null;
  status: string;
  createdAt: string;
};

type DashboardResponse = {
  stats: DashboardStats;
  checklist: DashboardChecklist;
  activeReleaseHighlights: DashboardReleaseSummary[];
  recentActivity: DashboardActivityItem[];
};

@Component({
  standalone: true,
  selector: 'app-dashboard',
  imports: [CommonModule, RouterLink],
  templateUrl: './dashboard.page.html'
})
export class DashboardPage {
  protected readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);
  private readonly selectedOrg = inject(SelectedOrgService);

  protected readonly data = signal<DashboardResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.selectedOrg.selectedOrgId();
      void this.reload();
    });
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const params: Record<string, string> = {};
      const orgId = this.selectedOrg.selectedOrgId();
      if (orgId) params['organizationId'] = orgId;

      const res = await firstValueFrom(
        this.http.get<DashboardResponse>('/api/dashboard', { params })
      );
      this.data.set(res);
    } catch {
      this.error.set('Could not load dashboard. Try again in a moment.');
      this.data.set(null);
    } finally {
      this.loading.set(false);
    }
  }

  /** Once every setup step is done the checklist is hidden. */
  protected checklistComplete(c: DashboardChecklist): boolean {
    return c.hasAzureOrganization && c.hasTeam && c.hasRegisteredRepository && c.hasRelease;
  }

  protected checklistPercent(c: DashboardChecklist): number {
    const n = [c.hasAzureOrganization, c.hasTeam, c.hasRegisteredRepository, c.hasRelease].filter(Boolean).length;
    return Math.round((n / 4) * 100);
  }

  protected activeReleasesDelta(s: DashboardStats): string {
    if (s.activeReleasesCount === 0) return 'No active releases';
    return 'Draft + active';
  }

  protected openPrsDelta(s: DashboardStats): string {
    if (s.openPullRequestsCount === 0) return 'All clear';
    return `${s.openPullRequestsCount} on active work`;
  }

  protected attentionDelta(s: DashboardStats): string {
    if (s.pullRequestsNeedingAttentionCount === 0) return 'All clear';
    return 'Open on draft/active';
  }

  protected reposDelta(s: DashboardStats): string {
    const p = s.distinctAzureDevOpsProjectsCount;
    if (s.registeredRepositoriesCount === 0) return 'across 0 ADO projects';
    return `across ${p} ADO project${p === 1 ? '' : 's'}`;
  }
}
