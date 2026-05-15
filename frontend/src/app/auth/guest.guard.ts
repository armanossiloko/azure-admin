import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';
import { AuthService } from './auth.service';

export const guestGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  await auth.ensureSession();
  if (auth.user()) {
    await router.navigate(['/dashboard']);
    return false;
  }
  return true;
};
