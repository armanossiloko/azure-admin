import { CommonModule } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { AppSettings, SettingsService } from '../../services/settings.service';

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

  protected form: AppSettings = {
    conventionalCommitsEnabled: false,
    conventionalCommitsUseEmojis: true,
    conventionalCommitsShowOtherGroup: true,
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
      this.form = { ...settings };
    } catch {
      this.error.set('Failed to load settings.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    this.error.set(null);
    this.saved.set(false);
    try {
      const updated = await firstValueFrom(this.settingsService.updateSettings(this.form));
      this.form = { ...updated };
      this.saved.set(true);
      setTimeout(() => this.saved.set(false), 3000);
    } catch {
      this.error.set('Failed to save settings.');
    } finally {
      this.saving.set(false);
    }
  }
}
