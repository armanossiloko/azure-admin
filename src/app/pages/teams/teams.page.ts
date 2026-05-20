import { CommonModule } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

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
export class TeamsPage implements OnInit {
  protected readonly teams = signal<Team[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly info = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly newName = signal('');
  protected readonly newParentId = signal<string>('');

  constructor(private readonly http: HttpClient) {}

  async ngOnInit(): Promise<void> {
    await this.refresh();
  }

  protected async refresh(): Promise<void> {
    this.error.set(null);
    try {
      const rows = await firstValueFrom(this.http.get<Team[]>('/api/teams'));
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
