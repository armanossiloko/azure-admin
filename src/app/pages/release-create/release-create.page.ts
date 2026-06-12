import { CommonModule } from '@angular/common';
import { SelectedOrgService } from '../../services/selected-org.service';
import { Component, computed, effect, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

type Team = {
  id: string;
  name: string;
  parentTeamId: string | null;
};

type RegisteredRepository = {
  id: string;
  azureDevOpsOrganization: string;
  azureDevOpsProject: string;
  repositoryIdOrName: string;
  serviceName: string | null;
  teamId: string;
};

type ReleaseSummary = {
  id: string;
  title: string;
  sprintLabel: string | null;
  status: string;
  createdAt: string;
};

type BatchCreateResponse = {
  results: { repositoryIdOrName: string; pullRequestId: number; url: string }[];
};

type ReleasePrPhase = 'DevToMaster' | 'MasterToProd';

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

  protected readonly teams = signal<Team[]>([]);
  protected readonly includedTeamIds = signal<ReadonlySet<string>>(new Set());
  protected readonly reposByTeam = signal<Map<string, RegisteredRepository[]>>(new Map());
  protected readonly selectedRepoIdsByTeam = signal<Map<string, Set<string>>>(new Map());

  protected readonly isSubmitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly created = signal<BatchCreateResponse | null>(null);

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
    return this.phase() === 'DevToMaster'
      ? `Release dev into master - Release ${label}`
      : `Release master into prod - Release ${label}`;
  });

  protected readonly phaseHint = computed(() =>
    this.phase() === 'DevToMaster'
      ? 'Default branches: dev → master (override below if needed).'
      : 'Default branches: master → prod (override below if needed).'
  );

  protected setPhase(v: string): void {
    this.phase.set(v === 'MasterToProd' ? 'MasterToProd' : 'DevToMaster');
    this.fromBranch.set('');
    this.toBranch.set('');
  }

  protected readonly selectedOrg = inject(SelectedOrgService);

  constructor(
    private readonly http: HttpClient,
    private readonly route: ActivatedRoute
  ) {
    effect(() => {
      this.selectedOrg.selectedOrgId();
      this.reposByTeam.set(new Map());
      this.selectedRepoIdsByTeam.set(new Map());
      this.reposLoadError.set(null);
      void this.reloadReposForIncludedTeams();
    });
  }

  async ngOnInit(): Promise<void> {
    await this.loadTeams();
    const releaseId = this.route.snapshot.paramMap.get('releaseId');
    if (releaseId) await this.loadExistingReleaseForBatch(releaseId);
  }

  private async loadExistingReleaseForBatch(id: string): Promise<void> {
    this.pageLoadError.set(null);
    this.releaseBlocked.set(null);
    try {
      const d = await firstValueFrom(this.http.get<ReleaseForBatchContext>(`/api/releases/${id}`));
      const st = (d.status || '').toLowerCase();
      if (st === 'completed' || st === 'archived') {
        this.releaseBlocked.set('This release is completed or archived; you cannot open new PR batches on it.');
      }
      this.existingRelease.set({
        id: d.id,
        title: d.title,
        sprintLabel: d.sprintLabel,
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
      const rows = await firstValueFrom(this.http.get<Team[]>('/api/teams'));
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

  protected reposForTeam(teamId: string): RegisteredRepository[] {
    return this.reposByTeam().get(teamId) ?? [];
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
      const params: Record<string, string> = { teamId };
      const orgId = this.selectedOrg.selectedOrgId();
      if (orgId) params['organizationId'] = orgId;
      const rows = await firstValueFrom(
        this.http.get<RegisteredRepository[]>('/api/registered-repositories', { params })
      );
      this.reposByTeam.update(m => new Map(m).set(teamId, rows ?? []));
    } catch (e: unknown) {
      this.reposByTeam.update(m => new Map(m).set(teamId, []));
      this.reposLoadError.set(this.prettyError(e));
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

  async submit(): Promise<void> {
    this.error.set(null);
    this.created.set(null);

    const included = [...this.includedTeamIds()];
    if (!included.length) {
      this.error.set('Include at least one team for this release.');
      return;
    }

    let anyRepo = false;
    for (const tid of included) {
      if ((this.selectedRepoIdsByTeam().get(tid)?.size ?? 0) > 0) {
        anyRepo = true;
        break;
      }
    }
    if (!anyRepo) {
      this.error.set('Select at least one repository for at least one included team.');
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
        const release = await firstValueFrom(
          this.http.post<ReleaseSummary>('/api/releases/find-or-create', {
            title: rt,
            sprintLabel: this.sprintLabel().trim() || null
          })
        );
        releaseId = release.id;
      }

      const phase = this.phase() === 'MasterToProd' ? 1 : 0;
      const batchBase = {
        phase,
        title: this.prTitle(),
        description: this.description().trim() || null,
        sourceBranch: this.fromBranch().trim() || null,
        targetBranch: this.toBranch().trim() || null
      };

      const teamErrors: string[] = [];

      for (const tid of included) {
        const ids = [...(this.selectedRepoIdsByTeam().get(tid) ?? [])];
        if (!ids.length) continue;
        try {
          const resp = await firstValueFrom(
            this.http.post<BatchCreateResponse>(
              `/api/releases/${releaseId}/teams/${tid}/pull-requests/batch`,
              { ...batchBase, registeredRepositoryIds: ids }
            )
          );
          aggregated.push(...(resp?.results ?? []));
        } catch (e: unknown) {
          const teamName = this.teams().find(t => t.id === tid)?.name ?? tid;
          teamErrors.push(`${teamName}: ${this.prettyError(e)}`);
        }
      }

      this.created.set({ results: aggregated });
      if (teamErrors.length) this.error.set(teamErrors.join(' · '));
    } catch (e: unknown) {
      this.error.set(this.prettyError(e));
    } finally {
      this.isSubmitting.set(false);
    }
  }

  private prettyError(e: unknown): string {
    const http = e as { error?: unknown; message?: string };
    const body = http?.error;
    const nested =
      typeof body === 'object' && body !== null && 'message' in body
        ? String((body as { message: unknown }).message)
        : null;
    return (
      nested ??
      (typeof body === 'string' ? body : null) ??
      http?.message ??
      'Request failed. Check backend logs for details.'
    );
  }

  private defaultSprintLabel(): string {
    const now = new Date();
    const d = new Date(Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()));
    const day = d.getUTCDay() || 7;
    d.setUTCDate(d.getUTCDate() + 4 - day);
    const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1));
    const weekNo = Math.ceil((((d.getTime() - yearStart.getTime()) / 86400000) + 1) / 7);
    const ww = String(weekNo).padStart(2, '0');
    return `${d.getUTCFullYear()}/${ww}`;
  }
}
