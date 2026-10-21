import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AzureDevOpsOrganizationsApi, AzureDevOpsOrganizationSummaryDto, apiErrorMessage } from '../../api';

@Component({
  standalone: true,
  selector: 'app-azure-organization-detail-page',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './azure-organization-detail.page.html'
})
export class AzureOrganizationDetailPage implements OnInit {
  private readonly organizationsApi = inject(AzureDevOpsOrganizationsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly org = signal<AzureDevOpsOrganizationSummaryDto | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly info = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly newPat = signal('');
  protected readonly newPatExpires = signal('');
  protected readonly editDisplay = signal('');

  protected patExpired(o: AzureDevOpsOrganizationSummaryDto): boolean {
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
      const o = await this.organizationsApi.azureDevOpsOrganizationsGet({ id });
      this.org.set(o);
      this.editDisplay.set(o.organizationDisplay);
      this.newPat.set('');
      this.newPatExpires.set(o.patExpiresAt ? o.patExpiresAt.slice(0, 10) : '');
    } catch (e: unknown) {
      this.error.set(apiErrorMessage(e));
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
      await this.organizationsApi.azureDevOpsOrganizationsUpdate({
        id: o.id,
        body: { organizationDisplay: this.editDisplay().trim() || null }
      });
      this.info.set('Organization details saved.');
      await this.reload();
    } catch (e: unknown) {
      this.error.set(apiErrorMessage(e));
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
    // The API requires the expiry (it drives the expiry notifications), so the form does too.
    if (!date) {
      this.error.set('PAT expiration date is required (copy it from the token settings in Azure DevOps).');
      return;
    }
    const patExpiresAt = `${date}T23:59:59.000Z`;
    if (new Date(patExpiresAt) <= new Date()) {
      this.error.set('PAT expiration must be in the future.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await this.organizationsApi.azureDevOpsOrganizationsUpsertPatCredential({
        id: o.id,
        body: { pat, patExpiresAt }
      });
      this.newPat.set('');
      this.info.set('PAT saved (encrypted on the server). Release PRs use your identity for this org.');
      await this.reload();
    } catch (e: unknown) {
      this.error.set(apiErrorMessage(e));
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
      await this.organizationsApi.azureDevOpsOrganizationsDeletePatCredential({ id: o.id });
      this.info.set('PAT removed.');
      await this.reload();
    } catch (e: unknown) {
      this.error.set(apiErrorMessage(e));
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
      await this.organizationsApi.azureDevOpsOrganizationsDelete({ id: o.id });
      await this.router.navigateByUrl('/settings/azure-organizations');
    } catch (e: unknown) {
      this.error.set(apiErrorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async deleteOrg(): Promise<void> {
    return this.removeOrganization();
  }
}
