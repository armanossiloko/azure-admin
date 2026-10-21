import { Injectable, inject } from '@angular/core';
import {
  CreateWebhookSubscriptionRequest,
  TestWebhookDraftRequest,
  UpdateWebhookSubscriptionRequest,
  WebhookDefaultTemplatesDto,
  WebhookDestinationKind,
  WebhookPlaceholderDto,
  WebhookSettingsDto,
  WebhookSubscriptionDto,
  WebhookTestResultDto,
  WebhooksApi
} from '../api';

/** Aliases over the generated contract so pages keep their short, domain-flavoured names. */
export type WebhookKind = WebhookDestinationKind;
export type WebhookPlaceholder = WebhookPlaceholderDto;
export type WebhookDefaultTemplates = WebhookDefaultTemplatesDto;
export type WebhookSubscription = WebhookSubscriptionDto;
export type WebhookSettings = WebhookSettingsDto;
export type CreateWebhookRequest = CreateWebhookSubscriptionRequest;
export type UpdateWebhookRequest = UpdateWebhookSubscriptionRequest;
export type WebhookTestResult = WebhookTestResultDto;
/** Sends a sample message from unsaved form values. When `url` is blank, `existingId`'s stored URL is used. */
export type { TestWebhookDraftRequest };

const emptyDraft = (): CreateWebhookRequest => ({
  name: '',
  kind: 'Teams',
  url: '',
  enabled: true,
  notifyOnDevToMasterCreated: true,
  notifyOnDevToMasterMerged: true,
  notifyOnMasterToProdCreated: true,
  notifyOnMasterToProdMerged: true,
  templateCreated: null,
  templateMerged: null
});

@Injectable({ providedIn: 'root' })
export class WebhooksService {
  private readonly api = inject(WebhooksApi);

  getSettings(): Promise<WebhookSettings> {
    return this.api.webhooksList();
  }

  create(request: CreateWebhookRequest): Promise<WebhookSubscription> {
    return this.api.webhooksCreate({ body: request });
  }

  update(id: string, request: UpdateWebhookRequest): Promise<WebhookSubscription> {
    return this.api.webhooksUpdate({ id, body: request });
  }

  delete(id: string): Promise<void> {
    return this.api.webhooksDelete({ id });
  }

  test(id: string, merged = false): Promise<WebhookTestResult> {
    return this.api.webhooksTest({ id, merged });
  }

  testDraft(request: TestWebhookDraftRequest): Promise<WebhookTestResult> {
    return this.api.webhooksTestDraft({ body: request });
  }

  emptyDraft(): CreateWebhookRequest {
    return emptyDraft();
  }
}
