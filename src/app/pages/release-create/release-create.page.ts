import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, OnInit, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  BatchCreateReleasePullRequestsRequest,
  CreatedPullRequestResult,
  RegisteredRepositoriesApi,
  RegisteredRepositoryDto,
  ReleasePrPhase,
  ReleasesApi,
  TeamDto,
  TeamPullRequestBatch,
  TeamsApi,
  apiErrorMessage
} from '../../api';
import { SelectedOrgService } from '../../services/selected-org.service';
import { SettingsService } from '../../services/settings.service';

type BatchCreateResponse = {
  results: CreatedPullRequestResult[];
  skippedDecommissionedRepositories?: string[];
  teamErrors?: string[];
};

type ReleaseForBatchContext = {
  id: string;
  title: string;
  sprintLabel: string | null;
  status: string;
};

@Component({
  standalone: true,
  selector: 'app-release-create-page',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './release-create.page.html'
})
export class ReleaseCreatePage implements OnInit {
  protected readonly sprintLabel = signal(this.defaultSprintLabel());
  protected readonly releaseTitle = signal(`Release sprint ${this.defaultSprintLabel()}`);
  protected readonly phase = signal<ReleasePrPhase>('DevToMaster');
  protected readonly fromBranch = signal('');
  protected readonly toBranch = signal('');
  protected readonly description = signal<string>('');

  protected readonly teams = signal<TeamDto[]>([]);
  protected readonly includedTeamIds = signal<ReadonlySet<string>>(new Set());
  protected readonly reposByTeam = signal<Map<string, RegisteredRepositoryDto[]>>(new Map());
  protected readonly selectedRepoIdsByTeam = signal<Map<string, Set<string>>>(new Map());

  protected readonly isSubmitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly created = signal<BatchCreateResponse | null>(null);
  protected readonly skippedDecommissioned = signal<string[]>([]);
  protected readonly preflightIssues = signal<string[]>([]);

  /** When set, PRs are added to this release directly (no find-or-create). */
  protected readonly existingRelease = signal<ReleaseForBatchContext | null>(null);
  /** Set when release status does not allow more batches. */
  protected readonly releaseBlocked = signal<string | null>(null);
  protected readonly pageLoadError = signal<string | null>(null);
  protected readonly reposLoadError = signal<string | null>(null);
  protected readonly reposLoading = signal(false);

  protected readonly prTitle = computed(() => {
    const s = this.sprintLabel().trim();
    const label = s || 'sprint ????/??';
    switch (this.phase()) {
      case 'ProdToDev':
        return `Release prod into dev - Release ${label}`;
      case 'MasterToProd':
        return `Release master into prod - Release ${label}`;
      default:
        return `Release dev into master - Release ${label}`;
    }
  });

  protected readonly phaseHint = computed(() => {
    switch (this.phase()) {
      case 'ProdToDev':
        return 'Default branches: prod → dev. Only available when prod is ahead of dev — this must finish before Dev → Master or Master → Prod.';
      case 'MasterToProd':
        return 'Default branches: master → prod (override below if needed). Requires a completed Dev → Master PR. Blocked while prod or master is ahead of dev.';
      default:
        return 'Default branches: dev → master (override below if needed). Blocked while prod or master is ahead of dev — use Prod → Dev first when prod has drifted.';
    }
  });

  protected readonly fromBranchPlaceholder = computed(() => {
    switch (this.phase()) {
      case 'ProdToDev':
        return 'default: prod';
      case 'MasterToProd':
        return 'default: master';
      default:
        return 'default: dev';
    }
  });

  protected readonly toBranchPlaceholder = computed(() => {
    switch (this.phase()) {
      case 'ProdToDev':
        return 'default: dev';
      case 'MasterToProd':
        return 'default: prod';
      default:
        return 'default: master';
    }
  });

  protected setPhase(v: string): void {
    if (v === 'ProdToDev') this.phase.set('ProdToDev');
    else if (v === 'MasterToProd') this.phase.set('MasterToProd');
    else this.phase.set('DevToMaster');
    this.fromBranch.set('');
    this.toBranch.set('');
  }

  protected readonly selectedOrg = inject(SelectedOrgService);
  private readonly settingsService = inject(SettingsService);
  private readonly releasesApi = inject(ReleasesApi);
  private readonly teamsApi = inject(TeamsApi);
  private readonly repositoriesApi = inject(RegisteredRepositoriesApi);
  private readonly route = inject(ActivatedRoute);

  constructor() {
    effect(() => {
      this.selectedOrg.selectedOrgId();
      this.reposByTeam.set(new Map());
      this.selectedRepoIdsByTeam.set(new Map());
      this.reposLoadError.set(null);
      untracked(() => void this.reloadReposForIncludedTeams());
    });
  }

  async ngOnInit(): Promise<void> {
    const releaseId = this.route.snapshot.paramMap.get('releaseId');
    if (!releaseId) void this.applySprintSize();
    await this.loadTeams();
    if (releaseId) await this.loadExistingReleaseForBatch(releaseId);
  }

  /** A release is created at the end of a sprint, so the default label is the week the sprint started. */
  private async applySprintSize(): Promise<void> {
    try {
      const { sprintWeeks } = await this.settingsService.getSettings();
      if (!sprintWeeks || sprintWeeks < 0) return;

      const current = this.defaultSprintLabel();
      const label = this.defaultSprintLabel(sprintWeeks);
      // Only replace the untouched defaults, never something the user has already typed.
      if (this.sprintLabel() === current) this.sprintLabel.set(label);
      if (this.releaseTitle() === `Release sprint ${current}`) this.releaseTitle.set(`Release sprint ${label}`);
    } catch {
      // Keep the current-week default when the settings cannot be loaded.
    }
  }

  private async loadExistingReleaseForBatch(id: string): Promise<void> {
    this.pageLoadError.set(null);
    this.releaseBlocked.set(null);
    try {
      const d = await this.releasesApi.releasesGet({ id });
      const st = (d.status || '').toLowerCase();
      if (st === 'completed' || st === 'archived') {
        this.releaseBlocked.set('This release is completed or archived; you cannot open new PR batches on it.');
      }
      this.existingRelease.set({
        id: d.id,
        title: d.title,
        sprintLabel: d.sprintLabel ?? null,
        status: d.status
      });
      this.releaseTitle.set(d.title);
      this.sprintLabel.set(d.sprintLabel?.trim() ?? '');
    } catch {
      this.pageLoadError.set('Could not load that release.');
    }
  }

  protected async loadTeams(): Promise<void> {
    try {
      const rows = await this.teamsApi.teamsList();
      this.teams.set(rows ?? []);
    } catch {
      this.teams.set([]);
    }
  }

  protected teamIncluded(teamId: string): boolean {
    return this.includedTeamIds().has(teamId);
  }

  protected async toggleTeamIncluded(teamId: string, checked: boolean): Promise<void> {
    this.includedTeamIds.update(s => {
      const n = new Set(s);
      if (checked) n.add(teamId);
      else n.delete(teamId);
      return n;
    });
    if (!checked) {
      this.selectedRepoIdsByTeam.update(m => {
        const next = new Map(m);
        next.delete(teamId);
        return next;
      });
      return;
    }
    await this.ensureReposLoaded(teamId);
  }

  protected reposForTeam(teamId: string): RegisteredRepositoryDto[] {
    return this.reposByTeam().get(teamId) ?? [];
  }

  protected activeReposForTeam(teamId: string): RegisteredRepositoryDto[] {
    return this.reposForTeam(teamId).filter(r => !r.isDecommissioned);
  }

  protected decommissionedReposForTeam(teamId: string): RegisteredRepositoryDto[] {
    return this.reposForTeam(teamId).filter(r => r.isDecommissioned);
  }

  private async reloadReposForIncludedTeams(): Promise<void> {
    for (const teamId of this.includedTeamIds()) {
      await this.ensureReposLoaded(teamId, true);
    }
  }

  protected async ensureReposLoaded(teamId: string, force = false): Promise<void> {
    if (!force && this.reposByTeam().has(teamId)) return;

    this.reposLoading.set(true);
    this.reposLoadError.set(null);
    this.reposByTeam.update(m => new Map(m).set(teamId, []));
    try {
      const rows = await this.repositoriesApi.registeredRepositoriesList({
        teamId,
        organizationId: this.selectedOrg.selectedOrgId() ?? undefined
      });
      this.reposByTeam.update(m => new Map(m).set(teamId, rows ?? []));
    } catch (e: unknown) {
      this.reposByTeam.update(m => new Map(m).set(teamId, []));
      this.reposLoadError.set(apiErrorMessage(e));
    } finally {
      this.reposLoading.set(false);
    }
  }

  protected toggleRepo(teamId: string, repoId: string): void {
    this.selectedRepoIdsByTeam.update(m => {
      const next = new Map<string, Set<string>>();
      for (const [k, v] of m) next.set(k, new Set(v));
      let set = next.get(teamId);
      if (!set) {
        set = new Set();
        next.set(teamId, set);
      }
      if (set.has(repoId)) set.delete(repoId);
      else set.add(repoId);
      return next;
    });
  }

  protected isRepoSelected(teamId: string, repoId: string): boolean {
    return this.selectedRepoIdsByTeam().get(teamId)?.has(repoId) ?? false;
  }

  protected selectedCountForTeam(teamId: string): number {
    return this.selectedRepoIdsByTeam().get(teamId)?.size ?? 0;
  }

  protected allReposSelectedForTeam(teamId: string): boolean {
    const repos = this.activeReposForTeam(teamId);
    if (!repos.length) return false;
    const selected = this.selectedRepoIdsByTeam().get(teamId);
    return repos.every(r => selected?.has(r.id));
  }

  protected toggleAllReposForTeam(teamId: string, checked: boolean): void {
    this.selectedRepoIdsByTeam.update(m => {
      const next = new Map<string, Set<string>>();
      for (const [k, v] of m) next.set(k, new Set(v));
      const repoIds = this.activeReposForTeam(teamId).map(r => r.id);
      next.set(teamId, checked ? new Set(repoIds) : new Set());
      return next;
    });
  }

  async submit(): Promise<void> {
    this.error.set(null);
    this.created.set(null);
    this.skippedDecommissioned.set([]);
    this.preflightIssues.set([]);

    const included = [...this.includedTeamIds()];
    if (!included.length) {
      this.error.set('Include at least one team for this release.');
      return;
    }

    let anyRepo = false;
    for (const tid of included) {
      const activeSelected = [...(this.selectedRepoIdsByTeam().get(tid) ?? [])].filter(id =>
        this.activeReposForTeam(tid).some(r => r.id === id)
      );
      if (activeSelected.length > 0) {
        anyRepo = true;
        break;
      }
    }
    if (!anyRepo) {
      this.error.set('Select at least one active repository. Decommissioned services are excluded from release pull requests.');
      return;
    }

    if (this.releaseBlocked()) return;

    const rt = this.releaseTitle().trim();
    if (!rt) {
      this.error.set('Release title is required.');
      return;
    }

    this.isSubmitting.set(true);
    const aggregated: BatchCreateResponse['results'] = [];

    try {
      const existing = this.existingRelease();
      let releaseId: string;
      if (existing) {
        releaseId = existing.id;
      } else {
        const release = await this.releasesApi.releasesFindOrCreateRelease({
          body: { title: rt, sprintLabel: this.sprintLabel().trim() || null }
        });
        releaseId = release.id;
      }

      const batchBase = {
        phase: this.phase(),
        title: this.prTitle(),
        description: this.description().trim() || null,
        sourceBranch: this.fromBranch().trim() || null,
        targetBranch: this.toBranch().trim() || null
      };

      const teamErrors: string[] = [];
      const skipped: string[] = [];
      const blocking: string[] = [];

      for (const tid of included) {
        const ids = [...(this.selectedRepoIdsByTeam().get(tid) ?? [])].filter(id => {
          const repo = this.reposForTeam(tid).find(r => r.id === id);
          return !repo?.isDecommissioned;
        });
        if (!ids.length) continue;

        const teamName = this.teams().find(t => t.id === tid)?.name ?? tid;
        const body: BatchCreateReleasePullRequestsRequest = { ...batchBase, registeredRepositoryIds: ids };

        try {
          const preflight = await this.releasesApi.releasesPreflightPullRequests({ releaseId, teamId: tid, body });

          for (const repo of preflight.repositories ?? []) {
            if (!repo.ok) {
              for (const issue of repo.issues ?? []) {
                blocking.push(`${teamName} / ${repo.serviceName || repo.repositoryIdOrName}: ${issue}`);
              }
            }
          }
        } catch (e: unknown) {
          blocking.push(`${teamName}: preflight failed — ${apiErrorMessage(e)}`);
        }
      }

      if (blocking.length) {
        this.preflightIssues.set(blocking);
        this.error.set('Preflight found problems. Fix them before opening pull requests.');
        return;
      }

      const teams: TeamPullRequestBatch[] = [];
      for (const tid of included) {
        const ids = [...(this.selectedRepoIdsByTeam().get(tid) ?? [])].filter(id => {
          const repo = this.reposForTeam(tid).find(r => r.id === id);
          return !repo?.isDecommissioned;
        });
        if (!ids.length) continue;
        teams.push({ teamId: tid, registeredRepositoryIds: ids });
      }

      try {
        const resp = await this.releasesApi.releasesBatchCreatePullRequestsForTeams({
          releaseId,
          body: { ...batchBase, teams }
        });
        aggregated.push(...(resp?.results ?? []));
        skipped.push(...(resp?.skippedDecommissionedRepositories ?? []));
        teamErrors.push(...(resp?.teamErrors ?? []));
      } catch (e: unknown) {
        teamErrors.push(apiErrorMessage(e));
      }

      this.created.set({ results: aggregated });
      this.skippedDecommissioned.set(skipped);
      if (teamErrors.length) this.error.set(teamErrors.join(' · '));
    } catch (e: unknown) {
      this.error.set(apiErrorMessage(e));
    } finally {
      this.isSubmitting.set(false);
    }
  }


  /** ISO calendar week label (yyyy/ww), `weeksBack` weeks before today (handles year boundaries). */
  private defaultSprintLabel(weeksBack = 0): string {
    const now = new Date();
    const d = new Date(Date.UTC(now.getFullYear(), now.getMonth(), now.getDate() - weeksBack * 7));
    const day = d.getUTCDay() || 7;
    d.setUTCDate(d.getUTCDate() + 4 - day);
    const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1));
    const weekNo = Math.ceil((((d.getTime() - yearStart.getTime()) / 86400000) + 1) / 7);
    const ww = String(weekNo).padStart(2, '0');
    return `${d.getUTCFullYear()}/${ww}`;
  }
}
