import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '../../auth/auth.service';

@Component({
  standalone: true,
  selector: 'app-login-page',
  templateUrl: './login.page.html',
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  protected signIn(): void {
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? undefined;
    this.auth.redirectToLogin(returnUrl);
  }
}
