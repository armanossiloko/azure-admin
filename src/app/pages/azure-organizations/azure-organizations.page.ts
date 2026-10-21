import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AzureDevOpsOrganizationsApi, AzureDevOpsOrganizationSummaryDto, apiErrorMessage } from '../../api';

@Component({
  standalone: true,
  selector: 'app-azure-organizations-page',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './azure-organizations.page.html'
})
export class AzureOrganizationsPage implements OnInit {
  private readonly organizationsApi = inject(AzureDevOpsOrganizationsApi);

  protected readonly rows = signal<AzureDevOpsOrganizationSummaryDto[]>([]);

  // Alias for template compatibility
  protected orgs = this.rows;
  protected readonly error = signal<string | null>(null);
  protected readonly info = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly newOrgKey = signal('');
  protected readonly newOrgDisplay = signal('');

  async ngOnInit(): Promise<void> {
    await this.refresh();
  }

  protected async refresh(): Promise<void> {
    this.error.set(null);
    try {
      const list = await this.organizationsApi.azureDevOpsOrganizationsList();
      this.rows.set(list ?? []);
    } catch (e: unknown) {
      this.error.set(apiErrorMessage(e));
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
      const created = await this.organizationsApi.azureDevOpsOrganizationsCreate({
        body: {
          organization: org,
          organizationDisplay: display || null,
          notes: null
        }
      });
      this.newOrgKey.set('');
      this.newOrgDisplay.set('');
      this.info.set('Organization added. Open it to add your PAT.');
      this.rows.update(rows =>
        [...rows, created].sort((a, b) => a.organizationDisplay.localeCompare(b.organizationDisplay))
      );
    } catch (e: unknown) {
      this.error.set(apiErrorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected patExpired(row: AzureDevOpsOrganizationSummaryDto): boolean {
    if (!row.hasPatCredential || !row.patExpiresAt) return false;
    return new Date(row.patExpiresAt) <= new Date();
  }
}
