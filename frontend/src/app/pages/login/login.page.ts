import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../auth/auth.service';

/** Defaults match the Development-only seed user in the API (`Program.cs`). */
const LOGIN_DEFAULTS = {
  email: 'you@test.local',
  password: 'password123',
} as const;

@Component({
  standalone: true,
  selector: 'app-login-page',
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './login.page.html'
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /** Reactive form so initial values always show in the DOM (template-driven ngModel can miss first paint). */
  readonly form = new FormGroup({
    email: new FormControl(LOGIN_DEFAULTS.email, {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
    password: new FormControl(LOGIN_DEFAULTS.password, {
      nonNullable: true,
      validators: [Validators.required, Validators.minLength(8)],
    }),
  });

  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly submitted = signal(false);

  protected async onSubmit(): Promise<void> {
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.error.set(null);
    this.loading.set(true);
    try {
      const { email, password } = this.form.getRawValue();
      await this.auth.login(email, password);
      const returnUrl = this.safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));
      await this.router.navigateByUrl(returnUrl);
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Sign-in failed. Check your email and password.');
    } finally {
      this.loading.set(false);
    }
  }

  /** Only same-origin app paths; avoids broken redirects after login. */
  private safeReturnUrl(raw: string | null): string {
    const fallback = '/dashboard';
    if (!raw?.trim()) return fallback;
    const u = raw.trim();
    if (!u.startsWith('/') || u.startsWith('//')) return fallback;
    return u;
  }
}
