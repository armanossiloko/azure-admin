import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface CurrentUser {
  id: string;
  email: string;
  displayName: string | null;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly user = signal<CurrentUser | null>(null);
  private sessionResolved = false;

  async ensureSession(): Promise<void> {
    if (this.sessionResolved) return;
    await this.refreshMe();
    this.sessionResolved = true;
  }

  resetSessionFlag(): void {
    this.sessionResolved = false;
  }

  async refreshMe(): Promise<void> {
    try {
      const me = await firstValueFrom(this.http.get<CurrentUser>('/api/auth/me'));
      this.user.set(me);
    } catch {
      this.user.set(null);
    }
  }

  redirectToLogin(returnUrl?: string): void {
    const url = returnUrl
      ? `/api/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`
      : '/api/auth/login';
    window.location.href = url;
  }

  async logout(): Promise<void> {
    window.location.href = '/api/auth/logout';
  }
}
