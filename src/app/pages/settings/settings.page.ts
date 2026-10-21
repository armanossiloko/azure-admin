import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { BrandingService, DEFAULT_BRANDING } from '../../services/branding.service';
import { AppSettings, SettingsService } from '../../services/settings.service';
import {
  CreateWebhookRequest,
  WebhookDefaultTemplates,
  WebhookKind,
  WebhookPlaceholder,
  WebhookSubscription,
  WebhooksService
} from '../../services/webhooks.service';

export type GroupConfig = { name: string; alwaysIncluded: boolean };

type WebhookDraft = CreateWebhookRequest & { customizeTemplates: boolean };

@Component({
  standalone: true,
  selector: 'app-settings-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './settings.page.html'
})
export class SettingsPage implements OnInit {
  private readonly brandingService = inject(BrandingService);
  private readonly webhooksService = inject(WebhooksService);

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

  protected readonly webhookKinds: WebhookKind[] = ['Teams', 'Slack', 'Discord'];
  protected readonly webhooks = signal<WebhookSubscription[]>([]);
  protected readonly webhookDefaults = signal<WebhookDefaultTemplates | null>(null);
  protected readonly webhookPlaceholders = signal<WebhookPlaceholder[]>([]);
  protected readonly webhookBusyId = signal<string | null>(null);
  protected readonly webhookError = signal<string | null>(null);
  protected readonly webhookNotice = signal<string | null>(null);
  protected readonly addingWebhook = signal(false);
  protected readonly editingWebhookId = signal<string | null>(null);
  protected webhookDraft: WebhookDraft = this.blankWebhookDraft();
  protected webhookUrlDraft = '';
  protected webhookEditUrl = '';
  protected webhookTestMerged = false;

  constructor(private readonly settingsService: SettingsService) {}

  async ngOnInit(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const [settings, hooks] = await Promise.all([
        firstValueFrom(this.settingsService.getSettings()),
        firstValueFrom(this.webhooksService.getSettings())
      ]);
      this.applyLoaded(settings);
      this.applyWebhooks(hooks);
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

  protected startAddWebhook(): void {
    this.webhookError.set(null);
    this.webhookNotice.set(null);
    this.editingWebhookId.set(null);
    this.addingWebhook.set(true);
    this.webhookDraft = this.blankWebhookDraft();
    this.webhookUrlDraft = '';
  }

  protected cancelWebhookForm(): void {
    this.addingWebhook.set(false);
    this.editingWebhookId.set(null);
    this.webhookDraft = this.blankWebhookDraft();
    this.webhookUrlDraft = '';
    this.webhookEditUrl = '';
  }

  protected startEditWebhook(hook: WebhookSubscription): void {
    this.webhookError.set(null);
    this.webhookNotice.set(null);
    this.addingWebhook.set(false);
    this.editingWebhookId.set(hook.id);
    this.webhookDraft = {
      name: hook.name,
      kind: hook.kind,
      url: '',
      enabled: hook.enabled,
      notifyOnDevToMasterCreated: hook.notifyOnDevToMasterCreated,
      notifyOnDevToMasterMerged: hook.notifyOnDevToMasterMerged,
      notifyOnMasterToProdCreated: hook.notifyOnMasterToProdCreated,
      notifyOnMasterToProdMerged: hook.notifyOnMasterToProdMerged,
      templateCreated: hook.templateCreated,
      templateMerged: hook.templateMerged,
      customizeTemplates: !!(hook.templateCreated || hook.templateMerged)
    };
    this.webhookEditUrl = '';
  }

  protected templateFor(kind: 'created' | 'merged'): string {
    const stored = kind === 'created' ? this.webhookDraft.templateCreated : this.webhookDraft.templateMerged;
    const defaults = this.webhookDefaults();
    return stored?.trim() || defaults?.[kind] || '';
  }

  protected setTemplate(kind: 'created' | 'merged', value: string): void {
    if (kind === 'created') this.webhookDraft.templateCreated = value;
    else this.webhookDraft.templateMerged = value;
  }

  protected resetTemplatesToDefault(): void {
    this.webhookDraft.templateCreated = null;
    this.webhookDraft.templateMerged = null;
  }

  protected onCustomizeTemplates(on: boolean): void {
    this.webhookDraft.customizeTemplates = on;
    if (!on) {
      this.resetTemplatesToDefault();
      return;
    }
    const defaults = this.webhookDefaults();
    this.webhookDraft.templateCreated ||= defaults?.created ?? null;
    this.webhookDraft.templateMerged ||= defaults?.merged ?? null;
  }

  protected enabledEventLabels(hook: WebhookSubscription): string[] {
    const labels: string[] = [];
    if (hook.notifyOnDevToMasterCreated) labels.push('dev → master created');
    if (hook.notifyOnDevToMasterMerged) labels.push('dev → master merged');
    if (hook.notifyOnMasterToProdCreated) labels.push('master → prod created');
    if (hook.notifyOnMasterToProdMerged) labels.push('master → prod merged');
    return labels;
  }

  protected async saveNewWebhook(): Promise<void> {
    this.webhookError.set(null);
    this.webhookNotice.set(null);
    this.webhookBusyId.set('new');
    try {
      const created = await firstValueFrom(
        this.webhooksService.create({
          ...this.payloadFromDraft(this.webhookUrlDraft.trim())
        })
      );
      this.webhooks.set([...this.webhooks(), created].sort((a, b) => a.name.localeCompare(b.name)));
      this.cancelWebhookForm();
      this.webhookNotice.set(`Added ${created.name}.`);
    } catch (e: unknown) {
      this.webhookError.set(this.prettyWebhookError(e, 'Could not add destination.'));
    } finally {
      this.webhookBusyId.set(null);
    }
  }

  protected async saveWebhook(hook: WebhookSubscription): Promise<void> {
    this.webhookError.set(null);
    this.webhookNotice.set(null);
    this.webhookBusyId.set(hook.id);
    try {
      const payload = this.payloadFromDraft(this.webhookEditUrl.trim() || null);
      const updated = await firstValueFrom(this.webhooksService.update(hook.id, payload));
      this.replaceWebhook(updated);
      this.cancelWebhookForm();
      this.webhookNotice.set(`Saved ${updated.name}.`);
    } catch (e: unknown) {
      this.webhookError.set(this.prettyWebhookError(e, 'Could not save destination.'));
    } finally {
      this.webhookBusyId.set(null);
    }
  }

  protected async toggleWebhookEnabled(hook: WebhookSubscription, enabled: boolean): Promise<void> {
    this.webhookError.set(null);
    this.webhookBusyId.set(hook.id);
    try {
      const updated = await firstValueFrom(
        this.webhooksService.update(hook.id, {
          name: hook.name,
          url: null,
          enabled,
          notifyOnDevToMasterCreated: hook.notifyOnDevToMasterCreated,
          notifyOnDevToMasterMerged: hook.notifyOnDevToMasterMerged,
          notifyOnMasterToProdCreated: hook.notifyOnMasterToProdCreated,
          notifyOnMasterToProdMerged: hook.notifyOnMasterToProdMerged,
          templateCreated: hook.templateCreated,
          templateMerged: hook.templateMerged
        })
      );
      this.replaceWebhook(updated);
    } catch (e: unknown) {
      this.webhookError.set(this.prettyWebhookError(e, 'Could not update destination.'));
    } finally {
      this.webhookBusyId.set(null);
    }
  }

  protected async testWebhook(hook: WebhookSubscription): Promise<void> {
    this.webhookError.set(null);
    this.webhookNotice.set(null);
    this.webhookBusyId.set(hook.id);
    try {
      const result = await firstValueFrom(this.webhooksService.test(hook.id));
      if (result.ok) this.webhookNotice.set(`Test message sent to ${hook.name}. Check the channel to confirm it arrived.`);
      else this.webhookError.set(result.error || 'Test delivery failed.');
      await this.reloadWebhooks();
    } catch (e: unknown) {
      this.webhookError.set(this.prettyWebhookError(e, 'Test delivery failed.'));
    } finally {
      this.webhookBusyId.set(null);
    }
  }

  /** Sends a sample message using the add/edit form values, without saving them. */
  protected async testWebhookDraft(existing: WebhookSubscription | null): Promise<void> {
    this.webhookError.set(null);
    this.webhookNotice.set(null);
    this.webhookBusyId.set(existing?.id ?? 'new');
    const url = (existing ? this.webhookEditUrl : this.webhookUrlDraft).trim();
    const payload = this.payloadFromDraft(url || null);
    const name = payload.name || existing?.name || payload.kind;
    try {
      const result = await firstValueFrom(
        this.webhooksService.testDraft({
          kind: existing?.kind ?? payload.kind,
          url: url || null,
          existingId: existing?.id ?? null,
          templateCreated: payload.templateCreated,
          templateMerged: payload.templateMerged,
          merged: this.webhookTestMerged
        })
      );
      if (result.ok) this.webhookNotice.set(`Test message sent to ${name}. Check the channel to confirm it arrived.`);
      else this.webhookError.set(result.error || 'Test delivery failed.');
    } catch (e: unknown) {
      this.webhookError.set(this.prettyWebhookError(e, 'Test delivery failed.'));
    } finally {
      this.webhookBusyId.set(null);
    }
  }

  protected async deleteWebhook(hook: WebhookSubscription): Promise<void> {
    if (!confirm(`Remove chat destination “${hook.name}”?`)) return;
    this.webhookError.set(null);
    this.webhookBusyId.set(hook.id);
    try {
      await firstValueFrom(this.webhooksService.delete(hook.id));
      this.webhooks.set(this.webhooks().filter((h) => h.id !== hook.id));
      if (this.editingWebhookId() === hook.id) this.cancelWebhookForm();
      this.webhookNotice.set(`Removed ${hook.name}.`);
    } catch (e: unknown) {
      this.webhookError.set(this.prettyWebhookError(e, 'Could not remove destination.'));
    } finally {
      this.webhookBusyId.set(null);
    }
  }

  protected kindHint(kind: WebhookKind): string {
    switch (kind) {
      case 'Teams':
        return 'Paste a Teams Workflows webhook URL (channel … → Workflows → “Send webhook alerts to a channel”).';
      case 'Slack':
        return 'Paste a Slack incoming webhook URL from hooks.slack.com.';
      case 'Discord':
        return 'Paste a Discord channel webhook URL (Channel → Integrations → Webhooks).';
    }
  }

  private payloadFromDraft(url: string | null): CreateWebhookRequest {
    const customize = this.webhookDraft.customizeTemplates;
    return {
      name: this.webhookDraft.name.trim(),
      kind: this.webhookDraft.kind,
      url: url ?? '',
      enabled: this.webhookDraft.enabled,
      notifyOnDevToMasterCreated: this.webhookDraft.notifyOnDevToMasterCreated,
      notifyOnDevToMasterMerged: this.webhookDraft.notifyOnDevToMasterMerged,
      notifyOnMasterToProdCreated: this.webhookDraft.notifyOnMasterToProdCreated,
      notifyOnMasterToProdMerged: this.webhookDraft.notifyOnMasterToProdMerged,
      templateCreated: customize ? this.webhookDraft.templateCreated : null,
      templateMerged: customize ? this.webhookDraft.templateMerged : null
    };
  }

  private replaceWebhook(updated: WebhookSubscription): void {
    this.webhooks.set(
      this.webhooks()
        .map((h) => (h.id === updated.id ? updated : h))
        .sort((a, b) => a.name.localeCompare(b.name))
    );
  }

  private async reloadWebhooks(): Promise<void> {
    const hooks = await firstValueFrom(this.webhooksService.getSettings());
    this.applyWebhooks(hooks);
  }

  private applyWebhooks(hooks: {
    destinations: WebhookSubscription[];
    defaults: WebhookDefaultTemplates;
    placeholders: WebhookPlaceholder[];
  }): void {
    this.webhooks.set(hooks.destinations ?? []);
    this.webhookDefaults.set(hooks.defaults);
    this.webhookPlaceholders.set(hooks.placeholders ?? []);
  }

  private blankWebhookDraft(): WebhookDraft {
    return { ...this.webhooksService.emptyDraft(), customizeTemplates: false };
  }

  private prettyWebhookError(e: unknown, fallback: string): string {
    const http = e as { error?: unknown };
    const body = http?.error;
    if (typeof body === 'object' && body !== null && 'message' in body) {
      const message = String((body as { message: unknown }).message ?? '').trim();
      if (message) return message;
    }
    return fallback;
  }
}
