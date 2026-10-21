import { Injectable, inject, signal } from '@angular/core';
import { AuthApi, CurrentUserDto } from '../api';

export type CurrentUser = CurrentUserDto;

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly authApi = inject(AuthApi);

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
      const me = await this.authApi.authMe();
      this.user.set(me);
    } catch {
      this.user.set(null);
    }
  }

  /** Full-page navigations: the login/logout endpoints answer with OIDC redirects, not JSON. */
  redirectToLogin(returnUrl?: string): void {
    const path = this.authApi.rootUrl + AuthApi.AuthLoginPath;
    window.location.href = returnUrl ? `${path}?returnUrl=${encodeURIComponent(returnUrl)}` : path;
  }

  async logout(): Promise<void> {
    window.location.href = this.authApi.rootUrl + AuthApi.AuthLogoutPath;
  }
}
