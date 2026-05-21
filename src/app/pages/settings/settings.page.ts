import { CommonModule } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { AppSettings, SettingsService } from '../../services/settings.service';

export type GroupConfig = { name: string; alwaysIncluded: boolean };

@Component({
  standalone: true,
  selector: 'app-settings-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './settings.page.html'
})
export class SettingsPage implements OnInit {
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal(false);

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
    jiraEnabled: false,
    jiraBaseUrl: null,
    jiraProjectKey: null
  };

  constructor(private readonly settingsService: SettingsService) {}

  async ngOnInit(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const settings = await firstValueFrom(this.settingsService.getSettings());
      this.form = { ...settings, excludedGroups: settings.excludedGroups ?? [] };
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

  protected async save(): Promise<void> {
    this.saving.set(true);
    this.error.set(null);
    this.saved.set(false);
    try {
      const updated = await firstValueFrom(this.settingsService.updateSettings(this.form));
      this.form = { ...updated, excludedGroups: updated.excludedGroups ?? [] };
      this.saved.set(true);
      setTimeout(() => this.saved.set(false), 3000);
    } catch {
      this.error.set('Failed to save settings.');
    } finally {
      this.saving.set(false);
    }
  }
}
