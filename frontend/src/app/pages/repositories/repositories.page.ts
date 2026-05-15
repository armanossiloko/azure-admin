import { CommonModule } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

type Team = { id: string; name: string };

type RegisteredRepository = {
  id: string;
  azureDevOpsOrganization: string;
  azureDevOpsProject: string;
  repositoryIdOrName: string;
  serviceName: string | null;
  teamId: string;
};

type AdoOrgSummary = {
  id: string;
  organizationKey: string;
  organizationDisplay: string;
  notes: string | null;
  hasPatCredential: boolean;
  patCredentialId: string | null;
  patUpdatedAt: string | null;
  patExpiresAt: string | null;
};

type CatalogProject = { id: string; name: string };
type CatalogRepo = { id: string; name: string; projectName: string };

@Component({
  standalone: true,
  selector: 'app-repositories-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './repositories.page.html'
})
export class RepositoriesPage implements OnInit {
  protected readonly teams = signal<Team[]>([]);
  protected readonly adoOrgs = signal<AdoOrgSummary[]>([]);
  protected readonly rows = signal<RegisteredRepository[]>([]);
  protected readonly filterTeamId = signal<string>('');
  protected readonly error = signal<string | null>(null);
  protected readonly info = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly org = signal('');
  protected readonly project = signal('');
  protected readonly repo = signal('');
  protected readonly serviceName = signal('');
  protected readonly registerTeamId = signal<string>('');

  protected readonly importOrgId = signal<string>('');
  protected readonly importProjects = signal<CatalogProject[]>([]);
  protected readonly importProjectName = signal<string>('');
  protected readonly importRepos = signal<CatalogRepo[]>([]);
  protected readonly importTeamId = signal<string>('');
  protected readonly importSelectedRepoIds = signal<ReadonlySet<string>>(new Set());
  protected readonly importCatalogBusy = signal(false);

  constructor(private readonly http: HttpClient) {}

  async ngOnInit(): Promise<void> {
    await Promise.all([this.loadTeams(), this.loadAdoOrgs()]);
    await this.refresh();
  }

  protected async loadTeams(): Promise<void> {
    try {
      const list = await firstValueFrom(this.http.get<Team[]>('/api/teams'));
      this.teams.set(list ?? []);
    } catch {
      this.teams.set([]);
    }
  }

  protected async loadAdoOrgs(): Promise<void> {
    try {
      const list = await firstValueFrom(this.http.get<AdoOrgSummary[]>('/api/azure-devops/organizations'));
      this.adoOrgs.set(list ?? []);
    } catch {
      this.adoOrgs.set([]);
    }
  }

  protected async refresh(): Promise<void> {
    this.error.set(null);
    const tid = this.filterTeamId().trim();
    try {
      const url = tid ? `/api/registered-repositories?teamId=${encodeURIComponent(tid)}` : '/api/registered-repositories';
      const list = await firstValueFrom(this.http.get<RegisteredRepository[]>(url));
      this.rows.set(list ?? []);
    } catch {
      this.error.set('Failed to load repositories.');
    }
  }

  protected selectedAdoOrg(): AdoOrgSummary | undefined {
    const id = this.importOrgId().trim();
    return this.adoOrgs().find(o => o.id === id);
  }

  protected async onImportOrgChange(): Promise<void> {
    this.importProjects.set([]);
    this.importProjectName.set('');
    this.importRepos.set([]);
    this.importSelectedRepoIds.set(new Set());
    const org = this.selectedAdoOrg();
    if (!org?.hasPatCredential) return;
    await this.loadImportProjects();
  }

  protected async loadImportProjects(): Promise<void> {
    const org = this.selectedAdoOrg();
    if (!org?.hasPatCredential) return;
    this.importCatalogBusy.set(true);
    this.error.set(null);
    try {
      const list = await firstValueFrom(
        this.http.get<CatalogProject[]>(`/api/azure-devops/catalog/organizations/${org.id}/projects`)
      );
      this.importProjects.set(list ?? []);
    } catch (e: unknown) {
      this.importProjects.set([]);
      this.error.set(this.fmtErr(e));
    } finally {
      this.importCatalogBusy.set(false);
    }
  }

  protected async onImportProjectChange(): Promise<void> {
    this.importRepos.set([]);
    this.importSelectedRepoIds.set(new Set());
    const org = this.selectedAdoOrg();
    const project = this.importProjectName().trim();
    if (!org?.hasPatCredential || !project) return;
    this.importCatalogBusy.set(true);
    this.error.set(null);
    try {
      const list = await firstValueFrom(
        this.http.get<CatalogRepo[]>(
          `/api/azure-devops/catalog/organizations/${org.id}/repositories?project=${encodeURIComponent(project)}`
        )
      );
      this.importRepos.set(list ?? []);
    } catch (e: unknown) {
      this.importRepos.set([]);
      this.error.set(this.fmtErr(e));
    } finally {
      this.importCatalogBusy.set(false);
    }
  }

  protected toggleImportRepo(repoId: string): void {
    this.importSelectedRepoIds.update(prev => {
      const n = new Set(prev);
      if (n.has(repoId)) n.delete(repoId);
      else n.add(repoId);
      return n;
    });
  }

  protected importRepoSelected(repoId: string): boolean {
    return this.importSelectedRepoIds().has(repoId);
  }

  protected isRepoAlreadyImported(repoName: string): boolean {
    const org = this.selectedAdoOrg();
    const projectName = this.importProjectName().trim();
    if (!org || !projectName) return false;
    
    return this.rows().some(
      r => r.azureDevOpsOrganization === org.organizationDisplay &&
           r.azureDevOpsProject === projectName &&
           r.repositoryIdOrName === repoName
    );
  }

  protected async importSelectedRepos(): Promise<void> {
    const teamId = this.importTeamId().trim();
    const org = this.selectedAdoOrg();
    const projectName = this.importProjectName().trim();
    const selectedIds = this.importSelectedRepoIds();

    if (!teamId) {
      this.error.set('Choose a team for the imported repositories.');
      return;
    }
    if (!org) {
      this.error.set('Choose an Azure DevOps organization.');
      return;
    }
    if (!org.hasPatCredential) {
      this.error.set('Save a PAT for that organization (Settings → Azure organizations) before importing.');
      return;
    }
    if (!projectName) {
      this.error.set('Choose a project.');
      return;
    }
    if (selectedIds.size === 0) {
      this.error.set('Select at least one repository.');
      return;
    }

    const byId = new Map(this.importRepos().map(r => [r.id, r]));
    const toImport = [...selectedIds].map(id => byId.get(id)).filter((r): r is CatalogRepo => !!r);

    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);

    let ok = 0;
    const failures: string[] = [];

    for (const r of toImport) {
      try {
        await firstValueFrom(
          this.http.post('/api/registered-repositories', {
            azureDevOpsOrganization: org.organizationDisplay,
            azureDevOpsProject: projectName,
            repositoryIdOrName: r.name,
            serviceName: null,
            teamId
          })
        );
        ok++;
      } catch (e: unknown) {
        failures.push(`${r.name}: ${this.fmtErr(e)}`);
      }
    }

    this.busy.set(false);
    if (ok > 0) {
      this.importSelectedRepoIds.set(new Set());
      this.info.set(
        failures.length
          ? `Registered ${ok} repo(s). ${failures.length} failed: ${failures.join(' ')}`
          : `Registered ${ok} repo(s).`
      );
      await this.refresh();
    } else {
      this.error.set(failures.join(' ') || 'Import failed.');
    }
  }

  protected async register(): Promise<void> {
    const teamId = this.registerTeamId().trim();
    if (!teamId || !this.org().trim() || !this.project().trim() || !this.repo().trim()) {
      this.error.set('Team, organization, project, and repository are required.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await firstValueFrom(
        this.http.post('/api/registered-repositories', {
          azureDevOpsOrganization: this.org().trim(),
          azureDevOpsProject: this.project().trim(),
          repositoryIdOrName: this.repo().trim(),
          serviceName: this.serviceName().trim() || null,
          teamId
        })
      );
      this.org.set('');
      this.project.set('');
      this.repo.set('');
      this.serviceName.set('');
      this.info.set('Repository registered.');
      await this.refresh();
    } catch (e: unknown) {
      this.error.set(this.fmtErr(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(row: RegisteredRepository): Promise<void> {
    if (!confirm(`Remove registration for “${row.repositoryIdOrName}”?`)) return;
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await firstValueFrom(this.http.delete(`/api/registered-repositories/${row.id}`));
      this.info.set('Removed.');
      await this.refresh();
    } catch (e: unknown) {
      this.error.set(this.fmtErr(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected teamLabel(id: string): string {
    return this.teams().find(t => t.id === id)?.name ?? id;
  }

  protected async onAliasBlur(row: RegisteredRepository, ev: Event): Promise<void> {
    const el = ev.target as HTMLInputElement;
    const v = el.value.trim();
    const current = (row.serviceName ?? '').trim();
    if (v === current) return;
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await firstValueFrom(
        this.http.patch(`/api/registered-repositories/${row.id}`, {
          serviceName: v || null
        })
      );
      this.info.set('Display name updated.');
      await this.refresh();
    } catch (e: unknown) {
      this.error.set(this.fmtErr(e));
      el.value = row.serviceName ?? '';
    } finally {
      this.busy.set(false);
    }
  }

  private fmtErr(e: unknown): string {
    const http = e as { error?: unknown };
    const body = http?.error;
    if (typeof body === 'object' && body !== null && 'message' in body) {
      return String((body as { message: unknown }).message);
    }
    if (typeof body === 'string') return body;
    return 'Request failed.';
  }
}
