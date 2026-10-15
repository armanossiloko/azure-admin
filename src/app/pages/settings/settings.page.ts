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
    sprintWeeks: 0,
    jiraEnabled: false,
    jiraBaseUrl: null,
    jiraProjectKeys: []
  };

  protected jiraProjectKeyDraft = '';

  constructor(private readonly settingsService: SettingsService) {}

  async ngOnInit(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const settings = await firstValueFrom(this.settingsService.getSettings());
      // Existing installs with a single stored key arrive as a one-element list.
      this.form = {
        ...settings,
        excludedGroups: settings.excludedGroups ?? [],
        jiraProjectKeys: settings.jiraProjectKeys ?? []
      };
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

  protected async save(): Promise<void> {
    this.saving.set(true);
    this.error.set(null);
    this.saved.set(false);
    try {
      // Flush any draft key the user typed but did not click Add for.
      this.addJiraProjectKey();
      const updated = await firstValueFrom(this.settingsService.updateSettings(this.form));
      this.form = {
        ...updated,
        excludedGroups: updated.excludedGroups ?? [],
        jiraProjectKeys: updated.jiraProjectKeys ?? []
      };
      this.saved.set(true);
      setTimeout(() => this.saved.set(false), 3000);
    } catch {
      this.error.set('Failed to save settings.');
    } finally {
      this.saving.set(false);
    }
  }
}
