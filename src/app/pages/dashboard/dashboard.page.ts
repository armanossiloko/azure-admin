import { CommonModule } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DashboardApi, DashboardChecklistDto, DashboardDto, DashboardStatsDto } from '../../api';
import { AuthService } from '../../auth/auth.service';
import { SelectedOrgService } from '../../services/selected-org.service';

@Component({
  standalone: true,
  selector: 'app-dashboard',
  imports: [CommonModule, RouterLink],
  templateUrl: './dashboard.page.html'
})
export class DashboardPage {
  protected readonly auth = inject(AuthService);
  private readonly dashboardApi = inject(DashboardApi);
  private readonly selectedOrg = inject(SelectedOrgService);

  protected readonly data = signal<DashboardDto | null>(null);
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
      const res = await this.dashboardApi.dashboardGet({
        organizationId: this.selectedOrg.selectedOrgId() ?? undefined
      });
      this.data.set(res);
    } catch {
      this.error.set('Could not load dashboard. Try again in a moment.');
      this.data.set(null);
    } finally {
      this.loading.set(false);
    }
  }

  /** Once every setup step is done the checklist is hidden. */
  protected checklistComplete(c: DashboardChecklistDto): boolean {
    return c.hasAzureOrganization && c.hasTeam && c.hasRegisteredRepository && c.hasRelease;
  }

  protected checklistPercent(c: DashboardChecklistDto): number {
    const n = [c.hasAzureOrganization, c.hasTeam, c.hasRegisteredRepository, c.hasRelease].filter(Boolean).length;
    return Math.round((n / 4) * 100);
  }

  protected activeReleasesDelta(s: DashboardStatsDto): string {
    if (s.activeReleasesCount === 0) return 'No active releases';
    return 'Draft + active';
  }

  protected openPrsDelta(s: DashboardStatsDto): string {
    if (s.openPullRequestsCount === 0) return 'All clear';
    return `${s.openPullRequestsCount} on active work`;
  }

  protected attentionDelta(s: DashboardStatsDto): string {
    if (s.pullRequestsNeedingAttentionCount === 0) return 'All clear';
    return 'Open on draft/active';
  }

  protected reposDelta(s: DashboardStatsDto): string {
    const p = s.distinctAzureDevOpsProjectsCount;
    if (s.registeredRepositoriesCount === 0) return 'across 0 ADO projects';
    return `across ${p} ADO project${p === 1 ? '' : 's'}`;
  }
}
