import { CommonModule } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { firstValueFrom } from 'rxjs';

type ReleaseTeam = { id: string; teamId: string; teamName: string };

type ReleasePullRequest = {
  id: string;
  teamId: string;
  teamName: string;
  registeredRepositoryId: string;
  serviceName: string | null;
  repositoryIdOrName: string;
  phase: string | number;
  status: string;
  azureDevOpsPullRequestId: number;
  url: string;
  sourceRefName: string;
  targetRefName: string;
  title: string;
  createdAt: string;
};

type CompletedPullRequestResult = {
  repositoryIdOrName: string;
  pullRequestId: number;
  success: boolean;
  message: string | null;
};

type CompleteBatchResponse = {
  results: CompletedPullRequestResult[];
};

type PullRequestStatusResult = { pullRequestId: string; status: string };

type StatusRefreshResponse = {
  results: PullRequestStatusResult[];
};

type ReleaseCommitItem = {
  commitId: string;
  comment: string;
  authorName: string;
  committedDate: string;
};

type JiraTicketRef = { key: string; url: string };

type EnrichedCommitItem = {
  commitId: string;
  authorName: string;
  committedDate: string;
  rawComment: string;
  conventionalType: string | null;
  scope: string | null;
  description: string;
  isBreaking: boolean;
  jiraReferences: JiraTicketRef[];
};

type CommitGroup = {
  groupName: string;
  isBreaking: boolean;
  commits: EnrichedCommitItem[];
};

type ReleaseRepositoryCommitNotes = {
  registeredRepositoryId: string;
  serviceName: string | null;
  repositoryIdOrName: string;
  phase: string | number;
  sourceRefName: string;
  targetRefName: string;
  fetchedAt: string;
  commits: ReleaseCommitItem[];
  commitGroups?: CommitGroup[] | null;
};

type ReleaseDetail = {
  id: string;
  title: string;
  sprintLabel: string | null;
  status: string;
  createdAt: string;
  teams: ReleaseTeam[];
  pullRequests: ReleasePullRequest[];
  repositoryCommitNotes?: ReleaseRepositoryCommitNotes[];
};

@Component({
  standalone: true,
  selector: 'app-release-detail-page',
  imports: [CommonModule, RouterLink],
  templateUrl: './release-detail.page.html',
  styles: [`
    :host ::ng-deep .md-preview h1 { font-size: 1.35em; font-weight: 700; margin: 0 0 12px; padding-bottom: 8px; border-bottom: 1px solid var(--border-subtle); }
    :host ::ng-deep .md-preview h2 { font-size: 1.15em; font-weight: 600; margin: 20px 0 8px; }
    :host ::ng-deep .md-preview h3 { font-size: 1.0em; font-weight: 600; margin: 16px 0 6px; }
    :host ::ng-deep .md-preview h4 { font-size: 0.8em; font-weight: 700; text-transform: uppercase; letter-spacing: 0.07em; color: var(--text-3); margin: 12px 0 6px; }
    :host ::ng-deep .md-preview ul { margin: 0 0 10px; padding-left: 18px; }
    :host ::ng-deep .md-preview li { margin: 3px 0; line-height: 1.55; }
    :host ::ng-deep .md-preview p { margin: 0 0 8px; }
    :host ::ng-deep .md-preview code { font-family: monospace; font-size: 0.85em; background: var(--bg-3); padding: 1px 5px; border-radius: 3px; }
    :host ::ng-deep .md-preview a { color: var(--accent); text-decoration: none; font-weight: 500; }
    :host ::ng-deep .md-preview a:hover { text-decoration: underline; }
    :host ::ng-deep .md-preview strong { font-weight: 600; }
  `]
})
export class ReleaseDetailPage implements OnInit {
  protected readonly prPhaseBuckets: { key: 'dev' | 'prod'; title: string }[] = [
    { key: 'dev', title: 'Dev → Master' },
    { key: 'prod', title: 'Master → Prod' }
  ];

  protected readonly release = signal<ReleaseDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly notesBusy = signal(false);
  protected readonly notesMessage = signal<string | null>(null);
  protected readonly copyMessage = signal<string | null>(null);
  protected readonly completingPhase = signal<'dev' | 'prod' | null>(null);
  protected readonly checkingStatusPhase = signal<'dev' | 'prod' | null>(null);
  protected readonly closingRelease = signal(false);
  protected readonly collapsedPhases = signal<ReadonlySet<'dev' | 'prod'>>(new Set());
  protected readonly collapsedNotePhases = signal<ReadonlySet<'dev' | 'prod'>>(new Set(['dev', 'prod']));
  protected readonly previewOpen = signal(false);
  protected readonly previewHtml = signal<SafeHtml>('');

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly http: HttpClient,
    private readonly sanitizer: DomSanitizer
  ) {}

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.error.set('Missing release id.');
      return;
    }
    if (id === 'new') {
      await this.router.navigate(['/releases/new'], { replaceUrl: true });
      return;
    }
    await this.loadRelease(id);
  }

  protected async loadRelease(id: string): Promise<void> {
    this.error.set(null);
    try {
      const data = await firstValueFrom(this.http.get<ReleaseDetail>(`/api/releases/${id}`));
      data.repositoryCommitNotes = (data.repositoryCommitNotes ?? []).map((n) => ({
        ...n,
        commits: n.commits ?? []
      }));
      this.release.set(data);
    } catch {
      this.error.set('Release not found or failed to load.');
      this.release.set(null);
    }
  }

  protected async refreshNotes(): Promise<void> {
    const rel = this.release();
    if (!rel) return;
    this.notesBusy.set(true);
    this.notesMessage.set(null);
    this.error.set(null);
    try {
      await firstValueFrom(this.http.post(`/api/releases/${rel.id}/commit-notes/refresh`, {}));
      await this.loadRelease(rel.id);
      this.notesMessage.set('Commit notes refreshed.');
    } catch {
      this.notesMessage.set(null);
      this.error.set('Could not refresh commit notes. Check PAT permissions and try again.');
    } finally {
      this.notesBusy.set(false);
    }
  }

  protected phaseCollapsed(key: 'dev' | 'prod'): boolean {
    return this.collapsedPhases().has(key);
  }

  protected togglePhaseCollapsed(key: 'dev' | 'prod'): void {
    this.collapsedPhases.update((s) => {
      const next = new Set(s);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  protected noteBucketCollapsed(key: 'dev' | 'prod'): boolean {
    return this.collapsedNotePhases().has(key);
  }

  protected toggleNoteBucketCollapsed(key: 'dev' | 'prod'): void {
    this.collapsedNotePhases.update((s) => {
      const next = new Set(s);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  protected isReleaseClosed(status: string): boolean {
    const s = (status || '').toLowerCase();
    return s === 'completed' || s === 'archived';
  }

  protected async closeRelease(): Promise<void> {
    const rel = this.release();
    if (!rel) return;
    if (!confirm(`Close release "${rel.title}"? No further PR batches can be opened on it afterwards.`)) return;

    this.closingRelease.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.http.post(`/api/releases/${rel.id}/close`, {}));
      this.release.update((r) => (r ? { ...r, status: 'Completed' } : r));
      this.copyMessage.set('Release closed.');
    } catch (e: unknown) {
      this.error.set(this.prettyError(e));
    } finally {
      this.closingRelease.set(false);
    }
  }

  protected prStatusBadgeClass(status: string): string {
    switch (status) {
      case 'Completed':
        return 'badge ok';
      case 'Active':
        return 'badge active';
      case 'Abandoned':
        return 'badge danger';
      default:
        return 'badge draft';
    }
  }

  protected async checkPrStatuses(phaseKey: 'dev' | 'prod'): Promise<void> {
    const rel = this.release();
    if (!rel) return;

    this.checkingStatusPhase.set(phaseKey);
    this.error.set(null);
    this.copyMessage.set(null);
    try {
      const phase = phaseKey === 'prod' ? 'MasterToProd' : 'DevToMaster';
      await firstValueFrom(
        this.http.post<StatusRefreshResponse>(`/api/releases/${rel.id}/pull-requests/status-refresh`, { phase })
      );
      await this.loadRelease(rel.id);
      this.copyMessage.set('PR statuses updated from Azure DevOps.');
    } catch (e: unknown) {
      this.error.set(this.prettyError(e));
    } finally {
      this.checkingStatusPhase.set(null);
    }
  }

  protected repoDisplay(pr: ReleasePullRequest): string {
    return pr.serviceName?.trim() || pr.repositoryIdOrName;
  }

  protected repoDisplayNotes(block: ReleaseRepositoryCommitNotes): string {
    return block.serviceName?.trim() || block.repositoryIdOrName;
  }

  protected phaseLabel(phase: string | number): string {
    if (phase === 'DevToMaster' || phase === 0 || phase === '0') return 'Dev → Master';
    if (phase === 'MasterToProd' || phase === 1 || phase === '1') return 'Master → Prod';
    return String(phase);
  }

  /** Buckets API enum / JSON numeric phase for grouping in the UI. */
  protected phaseBucket(phase: string | number): 'dev' | 'prod' | 'other' {
    if (phase === 'DevToMaster' || phase === 0 || phase === '0') return 'dev';
    if (phase === 'MasterToProd' || phase === 1 || phase === '1') return 'prod';
    return 'other';
  }

  protected pullRequestsInPhase(
    prs: ReleasePullRequest[],
    bucket: 'dev' | 'prod' | 'other'
  ): ReleasePullRequest[] {
    return prs
      .filter((p) => this.phaseBucket(p.phase) === bucket)
      .sort((a, b) => {
        const t = a.teamName.localeCompare(b.teamName);
        if (t !== 0) return t;
        return a.repositoryIdOrName.localeCompare(b.repositoryIdOrName);
      });
  }

  protected commitNotesInPhase(
    notes: ReleaseRepositoryCommitNotes[] | undefined,
    bucket: 'dev' | 'prod' | 'other'
  ): ReleaseRepositoryCommitNotes[] {
    if (!notes?.length) return [];
    return notes
      .filter((b) => this.phaseBucket(b.phase) === bucket)
      .sort((a, b) => this.repoDisplayNotes(a).localeCompare(this.repoDisplayNotes(b)));
  }

  protected commitBlockTrackKey(block: ReleaseRepositoryCommitNotes): string {
    return `${block.registeredRepositoryId}:${String(block.phase)}:${block.sourceRefName}:${block.targetRefName}`;
  }

  protected commitsForBlock(block: ReleaseRepositoryCommitNotes): ReleaseCommitItem[] {
    return block.commits ?? [];
  }

  protected hasCommits(block: ReleaseRepositoryCommitNotes): boolean {
    return this.commitsForBlock(block).length > 0;
  }

  protected async copyPrLinks(phaseKey: 'dev' | 'prod'): Promise<void> {
    const rel = this.release();
    if (!rel) return;
    const prs = this.pullRequestsInPhase(rel.pullRequests, phaseKey);
    if (!prs.length) {
      this.copyMessage.set('No PRs in this phase yet.');
      return;
    }

    const byTeam = new Map<string, string[]>();
    for (const pr of prs) {
      const urls = byTeam.get(pr.teamName) ?? [];
      urls.push(pr.url);
      byTeam.set(pr.teamName, urls);
    }
    const text = [...byTeam.entries()].map(([team, urls]) => `${team}:\n${urls.join('\n')}`).join('\n\n');

    try {
      await navigator.clipboard.writeText(text);
      this.copyMessage.set('Copied PR links to clipboard.');
    } catch {
      this.copyMessage.set('Could not copy to clipboard.');
    }
  }

  protected openAllPrs(phaseKey: 'dev' | 'prod'): void {
    const rel = this.release();
    if (!rel) return;
    for (const pr of this.pullRequestsInPhase(rel.pullRequests, phaseKey)) {
      window.open(pr.url, '_blank', 'noopener');
    }
  }

  protected async completeAllPrs(phaseKey: 'dev' | 'prod'): Promise<void> {
    const rel = this.release();
    if (!rel) return;
    const prs = this.pullRequestsInPhase(rel.pullRequests, phaseKey);
    if (!prs.length) return;

    const phaseTitle = this.prPhaseBuckets.find((b) => b.key === phaseKey)?.title ?? phaseKey;
    const confirmed = confirm(
      `Complete (rebase and fast-forward) all ${prs.length} PR${prs.length === 1 ? '' : 's'} in "${phaseTitle}"?\n\n` +
        'This merges directly in Azure DevOps and cannot be undone from here.'
    );
    if (!confirmed) return;

    this.completingPhase.set(phaseKey);
    this.error.set(null);
    this.copyMessage.set(null);
    try {
      const phase = phaseKey === 'prod' ? 'MasterToProd' : 'DevToMaster';
      const resp = await firstValueFrom(
        this.http.post<CompleteBatchResponse>(`/api/releases/${rel.id}/pull-requests/complete-batch`, { phase })
      );
      const results = resp?.results ?? [];
      const failed = results.filter((r) => !r.success);
      const succeeded = results.filter((r) => r.success);

      await this.loadRelease(rel.id);

      if (failed.length) {
        const detail = failed.map((f) => `${f.repositoryIdOrName}: ${f.message ?? 'failed'}`).join(' · ');
        this.error.set(`Completed ${succeeded.length}/${results.length} PRs in ${phaseTitle}. Failed — ${detail}`);
      } else {
        this.copyMessage.set(`Completed all ${succeeded.length} PR${succeeded.length === 1 ? '' : 's'} in ${phaseTitle}.`);
      }
    } catch (e: unknown) {
      this.error.set(this.prettyError(e));
    } finally {
      this.completingPhase.set(null);
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

  protected async copyMarkdown(scope: 'all' | 'phase' | 'repo', phaseKey?: 'dev' | 'prod', block?: ReleaseRepositoryCommitNotes): Promise<void> {
    const rel = this.release();
    if (!rel) return;

    let md = '';
    if (scope === 'repo' && block) {
      md = this.blockToMarkdown(rel.title, block);
    } else if (scope === 'phase' && phaseKey) {
      const bucket = this.prPhaseBuckets.find((b) => b.key === phaseKey);
      const blocks = this.commitNotesInPhase(rel.repositoryCommitNotes, phaseKey);
      md = this.phaseToMarkdown(rel.title, bucket?.title ?? phaseKey, blocks);
    } else {
      md = this.allNotesToMarkdown(rel);
    }

    if (!md.trim()) {
      this.copyMessage.set('Nothing to copy yet — refresh commit notes first.');
      return;
    }

    try {
      await navigator.clipboard.writeText(md);
      this.copyMessage.set('Copied markdown to clipboard.');
    } catch {
      this.copyMessage.set('Could not copy to clipboard.');
    }
  }

  private allNotesToMarkdown(rel: ReleaseDetail): string {
    const lines: string[] = [`# Release notes: ${rel.title}`, ''];
    if (rel.sprintLabel) lines.push(`Sprint: ${rel.sprintLabel}`, '');

    for (const bucket of this.prPhaseBuckets) {
      const blocks = this.commitNotesInPhase(rel.repositoryCommitNotes, bucket.key);
      if (!blocks.length) continue;
      lines.push(this.phaseToMarkdown(rel.title, bucket.title, blocks, false));
    }

    const other = this.commitNotesInPhase(rel.repositoryCommitNotes, 'other');
    if (other.length) {
      lines.push(this.phaseToMarkdown(rel.title, 'Other', other, false));
    }

    return lines.join('\n').trim();
  }

  private phaseToMarkdown(
    releaseTitle: string,
    phaseTitle: string,
    blocks: ReleaseRepositoryCommitNotes[],
    includeReleaseHeading = true
  ): string {
    const lines: string[] = [];
    if (includeReleaseHeading) {
      lines.push(`# Release notes: ${releaseTitle}`, '');
    }
    lines.push(`## ${phaseTitle}`, '');

    for (const block of blocks) {
      lines.push(this.blockToMarkdown(releaseTitle, block));
    }

    return lines.join('\n').trim();
  }

  private blockToMarkdown(_releaseTitle: string, block: ReleaseRepositoryCommitNotes): string {
    const lines: string[] = [];
    const repo = this.repoDisplayNotes(block);

    lines.push(`### ${repo}`, '');

    if (block.commitGroups?.length) {
      for (const group of block.commitGroups) {
        lines.push(`#### ${group.groupName}`, '');
        for (const c of group.commits) {
          const sha = c.commitId?.slice(0, 7) ?? '???????';
          const scope = c.scope ? `(**${c.scope}**) ` : '';
          const breaking = c.isBreaking ? ' **[BREAKING]**' : '';
          const jiraLinks = c.jiraReferences?.length
            ? ' ' + c.jiraReferences.map((j) => `[${j.key}](${j.url})`).join(' ')
            : '';
          lines.push(`- ${scope}${c.description}${jiraLinks}${breaking} (\`${sha}\`) — ${c.authorName}`);
        }
        lines.push('');
      }
      return lines.join('\n').trim();
    }

    const commits = this.commitsForBlock(block);
    if (!commits.length) {
      lines.push('_No commits loaded._', '');
      return lines.join('\n').trim();
    }

    for (const c of commits) {
      const sha = c.commitId?.slice(0, 7) ?? '???????';
      const when = c.committedDate ? new Date(c.committedDate).toLocaleString() : '';
      const author = c.authorName?.trim() || 'Unknown';
      const msg = (c.comment ?? '').trim().replace(/\r?\n/g, ' ');
      lines.push(`- ${msg} (\`${sha}\`) — ${author}${when ? `, ${when}` : ''}`);
    }
    lines.push('');
    return lines.join('\n').trim();
  }

  protected openPreview(): void {
    const rel = this.release();
    if (!rel) return;
    const md = this.allNotesToMarkdown(rel);
    this.previewHtml.set(this.sanitizer.bypassSecurityTrustHtml(this.mdToHtml(md)));
    this.previewOpen.set(true);
  }

  protected closePreview(): void {
    this.previewOpen.set(false);
  }

  private mdToHtml(md: string): string {
    const lines = md.split('\n');
    const out: string[] = [];
    let inList = false;

    for (const line of lines) {
      const headingMatch = line.match(/^(#{1,4}) (.+)/);
      if (headingMatch) {
        if (inList) { out.push('</ul>'); inList = false; }
        const level = headingMatch[1].length;
        out.push(`<h${level}>${this.fmtInline(headingMatch[2])}</h${level}>`);
      } else if (line.startsWith('- ')) {
        if (!inList) { out.push('<ul>'); inList = true; }
        out.push(`<li>${this.fmtInline(line.slice(2))}</li>`);
      } else if (line.trim() === '') {
        if (inList) { out.push('</ul>'); inList = false; }
      } else {
        if (inList) { out.push('</ul>'); inList = false; }
        out.push(`<p>${this.fmtInline(line)}</p>`);
      }
    }
    if (inList) out.push('</ul>');
    return out.join('\n');
  }

  private fmtInline(raw: string): string {
    // Extract markdown links before HTML-escaping so URLs stay intact
    const links: string[] = [];
    let s = raw.replace(/\[([^\]]*)\]\((https?:\/\/[^)]+)\)/g, (_, label, url) => {
      const safeLabel = label.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
      const safeUrl = url.replace(/"/g, '%22');
      links.push(`<a href="${safeUrl}" target="_blank" rel="noopener noreferrer">${safeLabel}</a>`);
      return `\x02${links.length - 1}\x02`;
    });

    // HTML-escape remaining text
    s = s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

    // Bold **text**
    s = s.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');

    // Inline code `text`
    s = s.replace(/`([^`]+)`/g, '<code>$1</code>');

    // Restore links
    s = s.replace(/\x02(\d+)\x02/g, (_, i) => links[Number(i)]);

    return s;
  }
}
