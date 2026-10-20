import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export type WebhookKind = 'Teams' | 'Slack' | 'Discord';

export type WebhookPlaceholder = { token: string; description: string };

export type WebhookDefaultTemplates = {
  created: string;
  merged: string;
  samplePreview: string;
};

export type WebhookSubscription = {
  id: string;
  name: string;
  kind: WebhookKind;
  enabled: boolean;
  urlPreview: string;
  notifyOnDevToMasterCreated: boolean;
  notifyOnDevToMasterMerged: boolean;
  notifyOnMasterToProdCreated: boolean;
  notifyOnMasterToProdMerged: boolean;
  templateCreated: string | null;
  templateMerged: string | null;
  createdAt: string;
  updatedAt: string;
  lastDeliveredAt: string | null;
  lastDeliveryError: string | null;
};

export type WebhookSettings = {
  destinations: WebhookSubscription[];
  defaults: WebhookDefaultTemplates;
  placeholders: WebhookPlaceholder[];
};

export type CreateWebhookRequest = {
  name: string;
  kind: WebhookKind;
  url: string;
  enabled: boolean;
  notifyOnDevToMasterCreated: boolean;
  notifyOnDevToMasterMerged: boolean;
  notifyOnMasterToProdCreated: boolean;
  notifyOnMasterToProdMerged: boolean;
  templateCreated: string | null;
  templateMerged: string | null;
};

export type UpdateWebhookRequest = Omit<CreateWebhookRequest, 'kind' | 'url'> & { url: string | null };

export type WebhookTestResult = { ok: boolean; error: string | null };

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
  constructor(private readonly http: HttpClient) {}

  getSettings(): Observable<WebhookSettings> {
    return this.http.get<WebhookSettings>('/api/settings/webhooks');
  }

  create(request: CreateWebhookRequest): Observable<WebhookSubscription> {
    return this.http.post<WebhookSubscription>('/api/settings/webhooks', request);
  }

  update(id: string, request: UpdateWebhookRequest): Observable<WebhookSubscription> {
    return this.http.put<WebhookSubscription>(`/api/settings/webhooks/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/settings/webhooks/${id}`);
  }

  test(id: string): Observable<WebhookTestResult> {
    return this.http.post<WebhookTestResult>(`/api/settings/webhooks/${id}/test`, {});
  }

  emptyDraft(): CreateWebhookRequest {
    return emptyDraft();
  }
}
