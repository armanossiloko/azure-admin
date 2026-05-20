import { inject } from '@angular/core';
import { type CanActivateFn } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  await auth.ensureSession();
  if (!auth.user()) {
    // Full-page redirect so the OIDC flow can set cookies correctly.
    auth.redirectToLogin(state.url);
    return false;
  }
  return true;
};

