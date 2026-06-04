import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../auth/auth.service';
import { SelectedOrgService } from '../../services/selected-org.service';
import { ThemeService, type ThemeId } from '../../theme/theme.service';

type AccountSettings = {
  userId: string;
  email: string;
  displayName: string | null;
  defaultOrganizationId: string | null;
  preferredTheme: string | null;
  notifyPatExpiry: boolean;
};

type NavOrg = {
  id: string;
  displayName: string;
};

@Component({
  standalone: true,
  selector: 'app-account-settings-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './account-settings.page.html'
})
export class AccountSettingsPage implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);
  private readonly selectedOrg = inject(SelectedOrgService);

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly info = signal<string | null>(null);

  protected readonly organizations = signal<NavOrg[]>([]);
  protected readonly defaultOrganizationId = signal<string>('');
  protected readonly preferredTheme = signal<ThemeId>('dark');
  protected readonly notifyPatExpiry = signal(true);

  async ngOnInit(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const [settings, nav] = await Promise.all([
        firstValueFrom(this.http.get<AccountSettings>('/api/account/settings')),
        firstValueFrom(this.http.get<{ organizations: NavOrg[] }>('/api/navigation'))
      ]);
      this.organizations.set(nav.organizations ?? []);
      this.defaultOrganizationId.set(settings.defaultOrganizationId ?? '');
      this.preferredTheme.set(settings.preferredTheme === 'light' ? 'light' : 'dark');
      this.notifyPatExpiry.set(settings.notifyPatExpiry);
    } catch {
      this.error.set('Could not load account settings.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    this.error.set(null);
    this.info.set(null);
    const orgId = this.defaultOrganizationId().trim();
    try {
      await firstValueFrom(
        this.http.patch('/api/account/settings', {
          updateDefaultOrganization: true,
          defaultOrganizationId: orgId || null,
          preferredTheme: this.preferredTheme(),
          notifyPatExpiry: this.notifyPatExpiry()
        })
      );
      this.theme.setTheme(this.preferredTheme());
      if (orgId) this.selectedOrg.select(orgId);
      this.info.set('Settings saved.');
    } catch {
      this.error.set('Could not save settings.');
    } finally {
      this.saving.set(false);
    }
  }
}
