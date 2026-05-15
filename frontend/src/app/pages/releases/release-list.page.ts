import { CommonModule } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

type ReleaseSummary = {
  id: string;
  title: string;
  sprintLabel: string | null;
  status: string;
  createdAt: string;
};

@Component({
  standalone: true,
  selector: 'app-release-list-page',
  imports: [CommonModule, RouterLink],
  templateUrl: './release-list.page.html'
})
export class ReleaseListPage implements OnInit {
  protected readonly rows = signal<ReleaseSummary[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  constructor(private readonly http: HttpClient) {}

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const list = await firstValueFrom(this.http.get<ReleaseSummary[]>('/api/releases'));
      this.rows.set(list ?? []);
    } catch {
      this.error.set('Failed to load releases.');
    } finally {
      this.loading.set(false);
    }
  }
}
