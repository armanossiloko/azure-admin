import { CommonModule } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SelectedOrgService } from '../../services/selected-org.service';

type Team = {
  id: string;
  name: string;
  parentTeamId: string | null;
};

@Component({
  standalone: true,
  selector: 'app-teams-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './teams.page.html'
})
export class TeamsPage {
  private readonly selectedOrg = inject(SelectedOrgService);

  protected readonly teams = signal<Team[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly info = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly newName = signal('');
  protected readonly newParentId = signal<string>('');

  protected readonly editingId = signal<string | null>(null);
  protected readonly editName = signal('');
  protected readonly editParentId = signal('');

  constructor(private readonly http: HttpClient) {
    effect(() => {
      this.selectedOrg.selectedOrgId();
      void this.refresh();
    });
  }

  protected async refresh(): Promise<void> {
    this.error.set(null);
    try {
      const params: Record<string, string> = {};
      const orgId = this.selectedOrg.selectedOrgId();
      if (orgId) params['organizationId'] = orgId;

      const rows = await firstValueFrom(this.http.get<Team[]>('/api/teams', { params }));
      this.teams.set(rows ?? []);
    } catch {
      this.error.set('Failed to load teams.');
    }
  }

  protected parentName(parentId: string | null): string {
    if (!parentId) return '—';
    const t = this.teams().find(x => x.id === parentId);
    return t?.name ?? parentId;
  }

  protected parentOptions(excludeId?: string | null): Team[] {
    return this.teams().filter(t => t.id !== excludeId);
  }

  protected startEdit(team: Team): void {
    this.editingId.set(team.id);
    this.editName.set(team.name);
    this.editParentId.set(team.parentTeamId ?? '');
    this.error.set(null);
    this.info.set(null);
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
  }

  protected async saveEdit(team: Team): Promise<void> {
    const name = this.editName().trim();
    if (!name) {
      this.error.set('Team name is required.');
      return;
    }

    const parent = this.editParentId().trim();
    const clearParent = !parent && !!team.parentTeamId;
    const parentChanged = parent !== (team.parentTeamId ?? '');

    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      const body: Record<string, unknown> = { name };
      if (clearParent) body['clearParent'] = true;
      else if (parentChanged && parent) body['parentTeamId'] = parent;

      await firstValueFrom(this.http.patch(`/api/teams/${team.id}`, body));
      this.editingId.set(null);
      this.info.set('Team updated.');
      await this.refresh();
    } catch (e: unknown) {
      this.error.set(this.fmtErr(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async create(): Promise<void> {
    const name = this.newName().trim();
    if (!name) {
      this.error.set('Team name is required.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      const pid = this.newParentId().trim();
      await firstValueFrom(
        this.http.post('/api/teams', {
          name,
          parentTeamId: pid ? pid : null
        })
      );
      this.newName.set('');
      this.newParentId.set('');
      this.info.set('Team created.');
      await this.refresh();
    } catch (e: unknown) {
      this.error.set(this.fmtErr(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(team: Team): Promise<void> {
    if (!confirm(`Delete team “${team.name}”?`)) return;
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await firstValueFrom(this.http.delete(`/api/teams/${team.id}`));
      this.info.set('Team removed.');
      await this.refresh();
    } catch (e: unknown) {
      this.error.set(this.fmtErr(e));
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
