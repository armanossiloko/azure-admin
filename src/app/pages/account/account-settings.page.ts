import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AccountSettingsApi, NavigationApi, NavigationOrganizationDto } from '../../api';
import { AuthService } from '../../auth/auth.service';
import { SelectedOrgService } from '../../services/selected-org.service';
import { ThemeService, type ThemeId, parseThemeId, THEME_OPTIONS } from '../../theme/theme.service';

@Component({
  standalone: true,
  selector: 'app-account-settings-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './account-settings.page.html'
})
export class AccountSettingsPage implements OnInit {
  private readonly accountApi = inject(AccountSettingsApi);
  private readonly navigationApi = inject(NavigationApi);
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);
  private readonly selectedOrg = inject(SelectedOrgService);

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly info = signal<string | null>(null);

  protected readonly organizations = signal<NavigationOrganizationDto[]>([]);
  protected readonly defaultOrganizationId = signal<string>('');
  protected readonly preferredTheme = signal<ThemeId>('dark');
  protected readonly themeOptions = THEME_OPTIONS;
  protected readonly notifyPatExpiry = signal(true);

  async ngOnInit(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const [settings, nav] = await Promise.all([
        this.accountApi.accountSettingsGetSettings(),
        this.navigationApi.navigationGet()
      ]);
      this.organizations.set(nav.organizations ?? []);
      this.defaultOrganizationId.set(settings.defaultOrganizationId ?? '');
      this.preferredTheme.set(parseThemeId(settings.preferredTheme ?? null) ?? 'dark');
      this.notifyPatExpiry.set(settings.notifyPatExpiry);
    } catch {
      this.error.set('Could not load account settings.');
    } finally {
      this.loading.set(false);
    }
  }

  protected onPreferredThemeChange(value: string): void {
    this.preferredTheme.set(parseThemeId(value) ?? 'dark');
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    this.error.set(null);
    this.info.set(null);
    const orgId = this.defaultOrganizationId().trim();
    try {
      await this.accountApi.accountSettingsUpdateSettings({
        body: {
          updateDefaultOrganization: true,
          defaultOrganizationId: orgId || null,
          preferredTheme: this.preferredTheme(),
          notifyPatExpiry: this.notifyPatExpiry()
        }
      });
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
