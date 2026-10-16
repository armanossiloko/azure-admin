import { CommonModule } from '@angular/common';
import { Component, inject, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '../../auth/auth.service';
import { BrandingService } from '../../services/branding.service';

@Component({
  standalone: true,
  selector: 'app-login-page',
  imports: [CommonModule],
  templateUrl: './login.page.html',
})
export class LoginPage implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  protected readonly branding = inject(BrandingService);

  protected readonly copyrightYear = new Date().getFullYear();

  ngOnInit(): void {
    void this.branding.load();
  }

  protected signIn(): void {
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? undefined;
    this.auth.redirectToLogin(returnUrl);
  }
}
