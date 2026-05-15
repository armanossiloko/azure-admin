import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

export interface CurrentUser {
  id: string;
  email: string;
  displayName: string | null;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

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

  async login(email: string, password: string): Promise<void> {
    try {
      await firstValueFrom(this.http.post<CurrentUser>('/api/auth/login', { email, password }));
    } catch (err) {
      throw this.toAuthError(err);
    }
    this.sessionResolved = true;
    await this.refreshMe();
  }

  async register(email: string, password: string, displayName: string | undefined): Promise<void> {
    try {
      await firstValueFrom(
        this.http.post<CurrentUser>('/api/auth/register', { email, password, displayName })
      );
    } catch (err) {
      throw this.toAuthError(err);
    }
    this.sessionResolved = true;
    await this.refreshMe();
  }

  private toAuthError(err: unknown): Error {
    if (err instanceof HttpErrorResponse) {
      const body = err.error;
      if (body && typeof body === 'object' && 'message' in body) {
        const msg = (body as { message: unknown }).message;
        if (typeof msg === 'string' && msg.trim()) return new Error(msg);
      }
      if (err.status === 0) {
        return new Error(
          'Could not reach the API. Start the backend and use ng serve (proxy), or open the app from the same host as the API.'
        );
      }
    }
    return new Error('Request failed.');
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/auth/logout', {}));
    } finally {
      this.user.set(null);
      this.sessionResolved = false;
      await this.router.navigate(['/login']);
    }
  }
}
