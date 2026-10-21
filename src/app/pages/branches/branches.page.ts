import { CommonModule } from '@angular/common';
import { Component, effect, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AdminActionLogApi,
  AdminActionLogDto,
  GitBranchDto,
  GitBranchesApi,
  RegisteredRepositoriesApi,
  RegisteredRepositoryDto,
  apiErrorMessage
} from '../../api';
import { SelectedOrgService } from '../../services/selected-org.service';

@Component({
  standalone: true,
  selector: 'app-branches-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './branches.page.html'
})
export class BranchesPage implements OnInit {
  private readonly selectedOrg = inject(SelectedOrgService);
  private readonly repositoriesApi = inject(RegisteredRepositoriesApi);
  private readonly branchesApi = inject(GitBranchesApi);
  private readonly actionLogApi = inject(AdminActionLogApi);

  protected readonly repos = signal<RegisteredRepositoryDto[]>([]);
  protected readonly branches = signal<GitBranchDto[]>([]);
  protected readonly actionLog = signal<AdminActionLogDto[]>([]);
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
    try {
      const list = await this.repositoriesApi.registeredRepositoriesList({
        organizationId: this.selectedOrg.selectedOrgId() ?? undefined
      });
      this.repos.set(list ?? []);
    } catch {
      this.repos.set([]);
    }
  }

  protected async refresh(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const list = await this.branchesApi.gitBranchesList({
        organizationId: this.selectedOrg.selectedOrgId() ?? undefined,
        registeredRepositoryId: this.filterRepoId().trim() || undefined,
        staleDays: this.staleDays(),
        staleOnly: this.staleOnly()
      });
      this.branches.set(list ?? []);
      if (!list?.length && this.staleOnly()) {
        this.info.set(`No stale branches found (older than ${this.staleDays()} days).`);
      } else {
        this.info.set(null);
      }
    } catch (e: unknown) {
      this.branches.set([]);
      this.error.set(apiErrorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async loadActionLog(): Promise<void> {
    this.logBusy.set(true);
    try {
      const list = await this.actionLogApi.adminActionLogList({ action: 'branch.delete', limit: 50 });
      this.actionLog.set(list ?? []);
    } catch {
      this.actionLog.set([]);
    } finally {
      this.logBusy.set(false);
    }
  }

  protected repoLabel(row: GitBranchDto): string {
    return row.serviceName?.trim() || row.repositoryIdOrName;
  }

  protected formatDate(iso: string | null | undefined): string {
    if (!iso) return '—';
    const d = new Date(iso);
    return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString(undefined, { dateStyle: 'medium' });
  }

  protected formatLogDate(iso: string): string {
    const d = new Date(iso);
    return Number.isNaN(d.getTime()) ? iso : d.toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' });
  }

  protected async deleteBranch(row: GitBranchDto): Promise<void> {
    const label = `${row.azureDevOpsOrganization}/${row.azureDevOpsProject}/${row.repositoryIdOrName}:${row.branchName}`;
    if (!confirm(`Delete branch “${row.branchName}” from ${this.repoLabel(row)}?\n\n${label}\n\nThis cannot be undone.`)) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await this.branchesApi.gitBranchesDelete({
        body: { registeredRepositoryId: row.registeredRepositoryId, branchName: row.branchName }
      });
      await Promise.all([this.refresh(), this.loadActionLog()]);
      this.info.set(`Deleted branch “${row.branchName}”.`);
    } catch (e: unknown) {
      this.error.set(apiErrorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
