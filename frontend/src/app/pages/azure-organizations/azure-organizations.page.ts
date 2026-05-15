import { CommonModule } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

type AzureDevOpsOrganizationSummary = {
  id: string;
  organizationKey: string;
  organizationDisplay: string;
  notes: string | null;
  hasPatCredential: boolean;
  patCredentialId: string | null;
  patUpdatedAt: string | null;
  patExpiresAt: string | null;
};

@Component({
  standalone: true,
  selector: 'app-azure-organizations-page',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './azure-organizations.page.html'
})
export class AzureOrganizationsPage implements OnInit {
  protected readonly rows = signal<AzureDevOpsOrganizationSummary[]>([]);
  
  // Alias for template compatibility
  protected orgs = this.rows;
  protected readonly error = signal<string | null>(null);
  protected readonly info = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly newOrgKey = signal('');
  protected readonly newOrgDisplay = signal('');

  constructor(private readonly http: HttpClient) {}

  async ngOnInit(): Promise<void> {
    await this.refresh();
  }

  protected async refresh(): Promise<void> {
    this.error.set(null);
    try {
      const list = await firstValueFrom(
        this.http.get<AzureDevOpsOrganizationSummary[]>('/api/azure-devops/organizations')
      );
      this.rows.set(list ?? []);
    } catch (e: unknown) {
      this.error.set(this.fmtErr(e));
    }
  }

  protected async create(): Promise<void> {
    const org = this.newOrgKey().trim();
    if (!org) {
      this.error.set('Organization name is required.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      const display = this.newOrgDisplay().trim();
      const created = await firstValueFrom(
        this.http.post<AzureDevOpsOrganizationSummary>('/api/azure-devops/organizations', {
          organization: org,
          organizationDisplay: display || null,
          notes: null,
        })
      );
      this.newOrgKey.set('');
      this.newOrgDisplay.set('');
      this.info.set('Organization added. Open it to add your PAT.');
      this.rows.update(rows => [...rows, created].sort((a, b) => a.organizationDisplay.localeCompare(b.organizationDisplay)));
    } catch (e: unknown) {
      this.error.set(this.fmtErr(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected patExpired(row: AzureDevOpsOrganizationSummary): boolean {
    if (!row.hasPatCredential || !row.patExpiresAt) return false;
    return new Date(row.patExpiresAt) <= new Date();
  }

  private fmtErr(e: unknown): string {
    const http = e as { error?: unknown; message?: string };
    const body = http?.error;
    if (typeof body === 'object' && body !== null && 'message' in body) {
      return String((body as { message: unknown }).message);
    }
    if (typeof body === 'string') return body;
    return http?.message ?? 'Request failed.';
  }
}
