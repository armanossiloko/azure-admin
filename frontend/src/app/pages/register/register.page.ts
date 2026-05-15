import { CommonModule } from '@angular/common';
import { Component, signal, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators, AbstractControl } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';

interface RegisterRequest {
  email: string;
  password: string;
  displayName?: string | null;
}

@Component({
  standalone: true,
  selector: 'app-register-page',
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './register.page.html'
})
export class RegisterPage {
  private readonly fb = inject(FormBuilder);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  protected readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    displayName: [''],
    password: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', Validators.required]
  }, {
    validators: this.passwordMatchValidator
  });

  protected readonly loading = signal(false);
  protected readonly submitted = signal(false);
  protected readonly error = signal<string | null>(null);

  private passwordMatchValidator(control: AbstractControl): { passwordMismatch: boolean } | null {
    const password = control.get('password')?.value;
    const confirmPassword = control.get('confirmPassword')?.value;
    return password && confirmPassword && password !== confirmPassword
      ? { passwordMismatch: true }
      : null;
  }

  protected async onSubmit(): Promise<void> {
    this.submitted.set(true);
    this.error.set(null);

    if (this.form.invalid) {
      return;
    }

    this.loading.set(true);

    try {
      const payload: RegisterRequest = {
        email: this.form.value.email!,
        password: this.form.value.password!,
        displayName: this.form.value.displayName || null
      };

      await this.http.post('/api/auth/register', payload).toPromise();
      await this.router.navigate(['/dashboard']);
    } catch (err) {
      if (err instanceof HttpErrorResponse) {
        this.error.set(err.error?.message || 'Registration failed. Please try again.');
      } else {
        this.error.set('An unexpected error occurred.');
      }
    } finally {
      this.loading.set(false);
    }
  }
}
