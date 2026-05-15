import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  await auth.ensureSession();
  if (!auth.user()) {
    router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
    return false;
  }
  return true;
};
