import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { BrandingService, DEFAULT_BRANDING } from '../../services/branding.service';
import { AppSettings, SettingsService } from '../../services/settings.service';

export type GroupConfig = { name: string; alwaysIncluded: boolean };

@Component({
  standalone: true,
  selector: 'app-settings-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './settings.page.html'
})
export class SettingsPage implements OnInit {
  private readonly brandingService = inject(BrandingService);

  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal(false);

  protected readonly brandingDefaults = DEFAULT_BRANDING;

  protected readonly allGroups: GroupConfig[] = [
    { name: 'Breaking Changes', alwaysIncluded: true },
    { name: 'Features', alwaysIncluded: false },
    { name: 'Bug Fixes', alwaysIncluded: false },
    { name: 'Performance', alwaysIncluded: false },
    { name: 'Refactoring', alwaysIncluded: false },
    { name: 'Documentation', alwaysIncluded: false },
    { name: 'Tests', alwaysIncluded: false },
    { name: 'Chores', alwaysIncluded: false },
    { name: 'Style', alwaysIncluded: false },
    { name: 'Reverts', alwaysIncluded: false },
    { name: 'Other', alwaysIncluded: false },
  ];

  protected form: AppSettings = {
    conventionalCommitsEnabled: false,
    conventionalCommitsUseEmojis: true,
    excludedGroups: [],
    sprintWeeks: 0,
    jiraEnabled: false,
    jiraBaseUrl: null,
    jiraProjectKeys: [],
    brandingApplicationName: null,
    brandingTagline: null,
    brandingDescription: null,
    branding: { ...DEFAULT_BRANDING }
  };

  /** Form-bound strings; empty means use defaults. */
  protected brandingName = '';
  protected brandingTagline = '';
  protected brandingDescription = '';

  protected jiraProjectKeyDraft = '';

  constructor(private readonly settingsService: SettingsService) {}

  async ngOnInit(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const settings = await firstValueFrom(this.settingsService.getSettings());
      this.applyLoaded(settings);
    } catch {
      this.error.set('Failed to load settings.');
    } finally {
      this.loading.set(false);
    }
  }

  protected isGroupIncluded(groupName: string): boolean {
    return !(this.form.excludedGroups ?? []).includes(groupName);
  }

  protected toggleGroup(groupName: string, included: boolean): void {
    const excluded = new Set(this.form.excludedGroups ?? []);
    if (included) {
      excluded.delete(groupName);
    } else {
      excluded.add(groupName);
    }
    this.form = { ...this.form, excludedGroups: [...excluded] };
  }

  protected addJiraProjectKey(): void {
    const key = this.jiraProjectKeyDraft.trim().toUpperCase();
    if (!key) return;

    const existing = this.form.jiraProjectKeys ?? [];
    if (existing.some((k) => k.toUpperCase() === key)) {
      this.jiraProjectKeyDraft = '';
      return;
    }

    this.form = { ...this.form, jiraProjectKeys: [...existing, key] };
    this.jiraProjectKeyDraft = '';
  }

  protected removeJiraProjectKey(key: string): void {
    this.form = {
      ...this.form,
      jiraProjectKeys: (this.form.jiraProjectKeys ?? []).filter((k) => k !== key)
    };
  }

  protected onJiraProjectKeyKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      this.addJiraProjectKey();
    }
  }

  protected resetBrandingToDefaults(): void {
    this.brandingName = '';
    this.brandingTagline = '';
    this.brandingDescription = '';
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    this.error.set(null);
    this.saved.set(false);
    try {
      // Flush any draft key the user typed but did not click Add for.
      this.addJiraProjectKey();
      // Always send branding strings (possibly ""). Empty clears the DB override so
      // config/built-in defaults apply — unlike null, which means "leave unchanged"
      // for older clients that omit the fields.
      const payload: AppSettings = {
        ...this.form,
        brandingApplicationName: this.brandingName.trim(),
        brandingTagline: this.brandingTagline.trim(),
        brandingDescription: this.brandingDescription.trim()
      };
      const updated = await firstValueFrom(this.settingsService.updateSettings(payload));
      this.applyLoaded(updated);
      this.brandingService.apply(updated.branding);
      this.saved.set(true);
      setTimeout(() => this.saved.set(false), 3000);
    } catch {
      this.error.set('Failed to save settings.');
    } finally {
      this.saving.set(false);
    }
  }

  private applyLoaded(settings: AppSettings): void {
    this.form = {
      ...settings,
      excludedGroups: settings.excludedGroups ?? [],
      jiraProjectKeys: settings.jiraProjectKeys ?? [],
      branding: settings.branding ?? { ...DEFAULT_BRANDING }
    };
    // Show stored overrides; empty fields mean defaults apply.
    this.brandingName = settings.brandingApplicationName ?? '';
    this.brandingTagline = settings.brandingTagline ?? '';
    this.brandingDescription = settings.brandingDescription ?? '';
  }
}
