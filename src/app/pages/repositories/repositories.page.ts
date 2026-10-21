import { CommonModule } from '@angular/common';
import { Component, effect, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AzureCatalogProjectDto,
  AzureCatalogRepositoryDto,
  AzureDevOpsCatalogApi,
  AzureDevOpsOrganizationsApi,
  AzureDevOpsOrganizationSummaryDto,
  PatchRegisteredRepositoryRequest,
  RegisteredRepositoriesApi,
  RegisteredRepositoryDto,
  TeamDto,
  TeamsApi,
  apiErrorMessage
} from '../../api';
import { SelectedOrgService } from '../../services/selected-org.service';

@Component({
  standalone: true,
  selector: 'app-repositories-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './repositories.page.html'
})
export class RepositoriesPage implements OnInit {
  private readonly selectedOrg = inject(SelectedOrgService);
  private readonly teamsApi = inject(TeamsApi);
  private readonly organizationsApi = inject(AzureDevOpsOrganizationsApi);
  private readonly catalogApi = inject(AzureDevOpsCatalogApi);
  private readonly repositoriesApi = inject(RegisteredRepositoriesApi);

  protected readonly teams = signal<TeamDto[]>([]);
  protected readonly adoOrgs = signal<AzureDevOpsOrganizationSummaryDto[]>([]);
  protected readonly rows = signal<RegisteredRepositoryDto[]>([]);
  protected readonly filterTeamId = signal<string>('');
  protected readonly statusFilter = signal('');
  protected readonly error = signal<string | null>(null);
  protected readonly info = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly org = signal('');
  protected readonly project = signal('');
  protected readonly repo = signal('');
  protected readonly serviceName = signal('');
  protected readonly registerTeamId = signal<string>('');

  protected readonly importOrgId = signal<string>('');
  protected readonly importProjects = signal<AzureCatalogProjectDto[]>([]);
  protected readonly importProjectName = signal<string>('');
  protected readonly importRepos = signal<AzureCatalogRepositoryDto[]>([]);
  protected readonly importTeamId = signal<string>('');
  protected readonly importSelectedRepoIds = signal<ReadonlySet<string>>(new Set());
  protected readonly importCatalogBusy = signal(false);

  constructor() {
    effect(() => {
      const id = this.selectedOrg.selectedOrgId();
      if (id) this.importOrgId.set(id);
      void this.refresh();
    });
  }

  async ngOnInit(): Promise<void> {
    await Promise.all([this.loadTeams(), this.loadAdoOrgs()]);
    const orgId = this.selectedOrg.selectedOrgId();
    if (orgId) this.importOrgId.set(orgId);
    await this.refresh();
  }

  protected async loadTeams(): Promise<void> {
    try {
      const list = await this.teamsApi.teamsList();
      this.teams.set(list ?? []);
    } catch {
      this.teams.set([]);
    }
  }

  protected async loadAdoOrgs(): Promise<void> {
    try {
      const list = await this.organizationsApi.azureDevOpsOrganizationsList();
      this.adoOrgs.set(list ?? []);
    } catch {
      this.adoOrgs.set([]);
    }
  }

  protected async refresh(): Promise<void> {
    this.error.set(null);
    try {
      const list = await this.repositoriesApi.registeredRepositoriesList({
        teamId: this.filterTeamId().trim() || undefined,
        organizationId: this.selectedOrg.selectedOrgId() ?? undefined
      });
      this.rows.set(list ?? []);
    } catch {
      this.error.set('Failed to load repositories.');
    }
  }

  protected selectedAdoOrg(): AzureDevOpsOrganizationSummaryDto | undefined {
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
      const list = await this.catalogApi.azureDevOpsCatalogListProjects({ organizationId: org.id });
      this.importProjects.set(list ?? []);
    } catch (e: unknown) {
      this.importProjects.set([]);
      this.error.set(apiErrorMessage(e));
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
      const list = await this.catalogApi.azureDevOpsCatalogListRepositories({ organizationId: org.id, project });
      this.importRepos.set(list ?? []);
    } catch (e: unknown) {
      this.importRepos.set([]);
      this.error.set(apiErrorMessage(e));
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
    const toImport = [...selectedIds].map(id => byId.get(id)).filter((r): r is AzureCatalogRepositoryDto => !!r);

    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);

    let ok = 0;
    const failures: string[] = [];

    for (const r of toImport) {
      try {
        await this.repositoriesApi.registeredRepositoriesRegister({
          body: {
            azureDevOpsOrganization: org.organizationDisplay,
            azureDevOpsProject: projectName,
            repositoryIdOrName: r.name,
            serviceName: null,
            teamId
          }
        });
        ok++;
      } catch (e: unknown) {
        failures.push(`${r.name}: ${apiErrorMessage(e)}`);
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
      await this.repositoriesApi.registeredRepositoriesRegister({
        body: {
          azureDevOpsOrganization: this.org().trim(),
          azureDevOpsProject: this.project().trim(),
          repositoryIdOrName: this.repo().trim(),
          serviceName: this.serviceName().trim() || null,
          teamId
        }
      });
      this.org.set('');
      this.project.set('');
      this.repo.set('');
      this.serviceName.set('');
      this.info.set('Repository registered.');
      await this.refresh();
    } catch (e: unknown) {
      this.error.set(apiErrorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(row: RegisteredRepositoryDto): Promise<void> {
    if (!confirm(`Remove registration for “${row.repositoryIdOrName}”?`)) return;
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await this.repositoriesApi.registeredRepositoriesDelete({ id: row.id });
      this.info.set('Removed.');
      await this.refresh();
    } catch (e: unknown) {
      this.error.set(apiErrorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected visibleRows(): RegisteredRepositoryDto[] {
    const filter = this.statusFilter().trim();
    const rows = this.rows();
    if (filter === 'active') return rows.filter(r => !r.isDecommissioned);
    if (filter === 'decommissioned') return rows.filter(r => r.isDecommissioned);
    return rows;
  }

  protected async setDecommissioned(row: RegisteredRepositoryDto, decommissioned: boolean): Promise<void> {
    const label = row.serviceName?.trim() || row.repositoryIdOrName;
    if (decommissioned && !confirm(`Decommission “${label}”? Future releases will not open pull requests for it.`)) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await this.patch(row, { isDecommissioned: decommissioned });
      this.info.set(decommissioned ? `${label} is decommissioned and will be skipped on the next release.` : `${label} is active again.`);
      await this.refresh();
    } catch (e: unknown) {
      this.error.set(apiErrorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected teamLabel(id: string): string {
    return this.teams().find(t => t.id === id)?.name ?? id;
  }

  protected async onTeamChange(row: RegisteredRepositoryDto, teamId: string): Promise<void> {
    if (!teamId || teamId === row.teamId) return;
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await this.patch(row, { teamId });
      this.info.set(`Moved ${row.serviceName?.trim() || row.repositoryIdOrName} to ${this.teamLabel(teamId)}.`);
      await this.refresh();
    } catch (e: unknown) {
      this.error.set(apiErrorMessage(e));
      await this.refresh();
    } finally {
      this.busy.set(false);
    }
  }

  protected async onAliasBlur(row: RegisteredRepositoryDto, ev: Event): Promise<void> {
    const el = ev.target as HTMLInputElement;
    const v = el.value.trim();
    const current = (row.serviceName ?? '').trim();
    if (v === current) return;
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await this.patch(row, { serviceName: v || null });
      this.info.set('Display name updated.');
      await this.refresh();
    } catch (e: unknown) {
      this.error.set(apiErrorMessage(e));
      el.value = row.serviceName ?? '';
    } finally {
      this.busy.set(false);
    }
  }

  private patch(row: RegisteredRepositoryDto, body: PatchRegisteredRepositoryRequest): Promise<RegisteredRepositoryDto> {
    return this.repositoriesApi.registeredRepositoriesPatch({ id: row.id, body });
  }
}
