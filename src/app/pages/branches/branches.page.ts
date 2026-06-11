import { CommonModule } from '@angular/common';
import { Component, effect, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SelectedOrgService } from '../../services/selected-org.service';

type RegisteredRepository = {
  id: string;
  azureDevOpsOrganization: string;
  azureDevOpsProject: string;
  repositoryIdOrName: string;
  serviceName: string | null;
  teamId: string;
};

type GitBranch = {
  registeredRepositoryId: string;
  azureDevOpsOrganization: string;
  azureDevOpsProject: string;
  repositoryIdOrName: string;
  serviceName: string | null;
  branchName: string;
  refName: string;
  objectId: string;
  lastCommitDate: string | null;
  daysSinceLastCommit: number | null;
  isProtected: boolean;
  isStale: boolean;
};

type AdminActionLog = {
  id: string;
  userId: string;
  userDisplayName: string | null;
  action: string;
  targetType: string;
  targetKey: string;
  detailsJson: string | null;
  success: boolean;
  errorMessage: string | null;
  createdAt: string;
};

@Component({
  standalone: true,
  selector: 'app-branches-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './branches.page.html'
})
export class BranchesPage implements OnInit {
  private readonly selectedOrg = inject(SelectedOrgService);
  private readonly http = inject(HttpClient);

  protected readonly repos = signal<RegisteredRepository[]>([]);
  protected readonly branches = signal<GitBranch[]>([]);
  protected readonly actionLog = signal<AdminActionLog[]>([]);
  protected readonly filterRepoId = signal('');
  protected readonly staleDays = signal(90);
  protected readonly staleOnly = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly info = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly logBusy = signal(false);

  constructor() {
    effect(() => {
      this.selectedOrg.selectedOrgId();
      void this.loadRepos();
    });
  }

  async ngOnInit(): Promise<void> {
    await Promise.all([this.loadRepos(), this.loadActionLog()]);
  }

  protected async loadRepos(): Promise<void> {
    const params = new URLSearchParams();
    const orgId = this.selectedOrg.selectedOrgId();
    if (orgId) params.set('organizationId', orgId);
    const qs = params.toString();
    const url = qs ? `/api/registered-repositories?${qs}` : '/api/registered-repositories';
    try {
      const list = await firstValueFrom(this.http.get<RegisteredRepository[]>(url));
      this.repos.set(list ?? []);
    } catch {
      this.repos.set([]);
    }
  }

  protected async refresh(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    const params = new URLSearchParams();
    const orgId = this.selectedOrg.selectedOrgId();
    if (orgId) params.set('organizationId', orgId);
    const repoId = this.filterRepoId().trim();
    if (repoId) params.set('registeredRepositoryId', repoId);
    params.set('staleDays', String(this.staleDays()));
    params.set('staleOnly', String(this.staleOnly()));
    try {
      const list = await firstValueFrom(this.http.get<GitBranch[]>(`/api/git/branches?${params}`));
      this.branches.set(list ?? []);
      if (!list?.length && this.staleOnly()) {
        this.info.set(`No stale branches found (older than ${this.staleDays()} days).`);
      } else {
        this.info.set(null);
      }
    } catch (e: unknown) {
      this.branches.set([]);
      this.error.set(this.fmtErr(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async loadActionLog(): Promise<void> {
    this.logBusy.set(true);
    try {
      const list = await firstValueFrom(
        this.http.get<AdminActionLog[]>('/api/admin/action-log?action=branch.delete&limit=50')
      );
      this.actionLog.set(list ?? []);
    } catch {
      this.actionLog.set([]);
    } finally {
      this.logBusy.set(false);
    }
  }

  protected repoLabel(row: GitBranch): string {
    return row.serviceName?.trim() || row.repositoryIdOrName;
  }

  protected formatDate(iso: string | null): string {
    if (!iso) return '—';
    const d = new Date(iso);
    return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString(undefined, { dateStyle: 'medium' });
  }

  protected formatLogDate(iso: string): string {
    const d = new Date(iso);
    return Number.isNaN(d.getTime()) ? iso : d.toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' });
  }

  protected async deleteBranch(row: GitBranch): Promise<void> {
    const label = `${row.azureDevOpsOrganization}/${row.azureDevOpsProject}/${row.repositoryIdOrName}:${row.branchName}`;
    if (!confirm(`Delete branch “${row.branchName}” from ${this.repoLabel(row)}?\n\n${label}\n\nThis cannot be undone.`)) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await firstValueFrom(
        this.http.delete('/api/git/branches', {
          body: {
            registeredRepositoryId: row.registeredRepositoryId,
            branchName: row.branchName
          }
        })
      );
      this.info.set(`Deleted branch “${row.branchName}”.`);
      await Promise.all([this.refresh(), this.loadActionLog()]);
    } catch (e: unknown) {
      this.error.set(this.fmtErr(e));
    } finally {
      this.busy.set(false);
    }
  }

  private fmtErr(e: unknown): string {
    const http = e as { error?: unknown };
    const body = http?.error;
    if (typeof body === 'object' && body !== null) {
      if ('message' in body) return String((body as { message: unknown }).message);
      if ('errorMessage' in body) return String((body as { errorMessage: unknown }).errorMessage);
    }
    if (typeof body === 'string') return body;
    return 'Request failed.';
  }
}
