import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
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
  selector: 'app-azure-organization-detail-page',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './azure-organization-detail.page.html'
})
export class AzureOrganizationDetailPage implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly org = signal<AzureDevOpsOrganizationSummary | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly info = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly newPat = signal('');
  protected readonly newPatExpires = signal('');
  protected readonly editDisplay = signal('');

  protected patExpired(o: AzureDevOpsOrganizationSummary): boolean {
    if (!o?.patExpiresAt) return false;
    return new Date(o.patExpiresAt) <= new Date();
  }

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected async reload(): Promise<void> {
    this.error.set(null);
    const id = this.route.snapshot.paramMap.get('orgId');
    if (!id) {
      this.error.set('Missing organization id.');
      return;
    }
    try {
      const o = await firstValueFrom(this.http.get<AzureDevOpsOrganizationSummary>(`/api/azure-devops/organizations/${id}`));
      this.org.set(o);
      this.editDisplay.set(o.organizationDisplay);
      this.newPat.set('');
      if (o.patExpiresAt) {
        this.newPatExpires.set(o.patExpiresAt.slice(0, 10));
      } else {
        this.newPatExpires.set('');
      }
    } catch (e: unknown) {
      this.error.set(this.fmtErr(e));
      this.org.set(null);
    }
  }

  protected async updateOrg(): Promise<void> {
    const o = this.org();
    if (!o) return;
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await firstValueFrom(
        this.http.put(`/api/azure-devops/organizations/${o.id}`, {
          organizationDisplay: this.editDisplay().trim() || null,
        })
      );
      this.info.set('Organization details saved.');
      await this.reload();
    } catch (e: unknown) {
      this.error.set(this.fmtErr(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async savePat(): Promise<void> {
    const o = this.org();
    if (!o) return;
    const pat = this.newPat().trim();
    const date = this.newPatExpires().trim();
    if (!pat) {
      this.error.set('PAT is required.');
      return;
    }
    const patExpiresAt = date ? `${date}T23:59:59.000Z` : null;
    if (patExpiresAt && new Date(patExpiresAt) <= new Date()) {
      this.error.set('PAT expiration must be in the future.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await firstValueFrom(
        this.http.put(`/api/azure-devops/organizations/${o.id}/pat-credential`, {
          pat,
          patExpiresAt,
        })
      );
      this.newPat.set('');
      this.info.set('PAT saved (encrypted on the server). Release PRs use your identity for this org.');
      await this.reload();
    } catch (e: unknown) {
      this.error.set(this.fmtErr(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async removePat(): Promise<void> {
    const o = this.org();
    if (!o || !o.hasPatCredential) return;
    if (!confirm('Remove the PAT for this organization? Release PRs will fail until you add a new one.')) return;
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await firstValueFrom(this.http.delete(`/api/azure-devops/organizations/${o.id}/pat-credential`));
      this.info.set('PAT removed.');
      await this.reload();
    } catch (e: unknown) {
      this.error.set(this.fmtErr(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async removeOrganization(): Promise<void> {
    const o = this.org();
    if (!o) return;
    if (!confirm(`Delete organization “${o.organizationDisplay}” and any stored PAT? This cannot be undone.`)) return;
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await firstValueFrom(this.http.delete(`/api/azure-devops/organizations/${o.id}`));
      await this.router.navigateByUrl('/settings/azure-organizations');
    } catch (e: unknown) {
      this.error.set(this.fmtErr(e));
    } finally {
      this.busy.set(false);
    }
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

  protected async deleteOrg(): Promise<void> {
    return this.removeOrganization();
  }
}
