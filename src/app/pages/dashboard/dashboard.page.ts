import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../auth/auth.service';

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
export class DashboardPage implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);

  protected readonly data = signal<DashboardResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const res = await firstValueFrom(this.http.get<DashboardResponse>('/api/dashboard'));
      this.data.set(res);
    } catch {
      this.error.set('Could not load dashboard. Try again in a moment.');
      this.data.set(null);
    } finally {
      this.loading.set(false);
    }
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
    return 'On draft releases';
  }

  protected reposDelta(s: DashboardStats): string {
    const p = s.distinctAzureDevOpsProjectsCount;
    if (s.registeredRepositoriesCount === 0) return 'across 0 ADO projects';
    return `across ${p} ADO project${p === 1 ? '' : 's'}`;
  }
}
