import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, HostListener, OnInit, inject, signal, viewChild, ElementRef } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { ThemeService } from '../theme/theme.service';

type DashboardActivityItem = {
  kind: string;
  title: string;
  subtitle: string | null;
  occurredAt: string;
  href: string | null;
};

type NavigationOrganization = {
  id: string;
  displayName: string;
  organizationKey: string;
  hasPatCredential: boolean;
};

type NavigationSummary = {
  organizations: NavigationOrganization[];
  activityPreview: DashboardActivityItem[];
  unreadNotificationsCount: number;
};

const SelectedOrgStorageKey = 'azure-admin.selected-org-id';

@Component({
  standalone: true,
  selector: 'app-shell',
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss'
})
export class AppShell implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);

  private readonly accountDropdown = viewChild<ElementRef<HTMLElement>>('accountDropdown');
  private readonly activityPanel = viewChild<ElementRef<HTMLElement>>('activityPanel');
  private readonly orgSwitcher = viewChild<ElementRef<HTMLElement>>('orgSwitcher');

  protected readonly sidebarMode = signal<'expanded' | 'icon'>('expanded');
  protected readonly accountMenuOpen = signal(false);
  protected readonly activityMenuOpen = signal(false);
  protected readonly orgMenuOpen = signal(false);

  protected readonly navigation = signal<NavigationSummary | null>(null);
  protected readonly navigationError = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.loadNavigation();
  }

  protected async loadNavigation(): Promise<void> {
    this.navigationError.set(null);
    try {
      const res = await firstValueFrom(this.http.get<NavigationSummary>('/api/navigation'));
      this.navigation.set(res);
      this.applyStoredOrgSelection();
    } catch {
      this.navigation.set(null);
      this.navigationError.set('Could not load navigation.');
    }
  }

  private applyStoredOrgSelection(): void {
    const nav = this.navigation();
    if (!nav?.organizations.length) {
      this.selectedOrgId.set(null);
      return;
    }
    let stored: string | null = null;
    try {
      stored = globalThis.sessionStorage?.getItem(SelectedOrgStorageKey) ?? null;
    } catch {
      stored = null;
    }
    const match = stored && nav.organizations.some(o => o.id === stored);
    this.selectedOrgId.set(match ? stored : nav.organizations[0].id);
  }

  protected readonly selectedOrgId = signal<string | null>(null);

  protected selectedOrganization(): NavigationOrganization | null {
    const nav = this.navigation();
    if (!nav?.organizations.length) return null;
    const id = this.selectedOrgId();
    if (id) {
      const found = nav.organizations.find(o => o.id === id);
      if (found) return found;
    }
    return nav.organizations[0];
  }

  protected selectOrganization(id: string): void {
    this.selectedOrgId.set(id);
    try {
      globalThis.sessionStorage?.setItem(SelectedOrgStorageKey, id);
    } catch {
      /* ignore */
    }
    this.orgMenuOpen.set(false);
  }

  protected toggleSidebar(): void {
    this.sidebarMode.update(mode => (mode === 'expanded' ? 'icon' : 'expanded'));
  }

  protected toggleTheme(): void {
    this.theme.setTheme(this.theme.theme() === 'dark' ? 'light' : 'dark');
  }

  protected toggleAccountMenu(): void {
    const next = !this.accountMenuOpen();
    this.accountMenuOpen.set(next);
    if (next) {
      this.activityMenuOpen.set(false);
      this.orgMenuOpen.set(false);
    }
  }

  protected closeAccountMenu(): void {
    this.accountMenuOpen.set(false);
  }

  protected toggleActivityMenu(): void {
    const next = !this.activityMenuOpen();
    this.activityMenuOpen.set(next);
    if (next) {
      this.accountMenuOpen.set(false);
      this.orgMenuOpen.set(false);
    }
  }

  protected closeActivityMenu(): void {
    this.activityMenuOpen.set(false);
  }

  protected toggleOrgMenu(): void {
    const nav = this.navigation();
    if (!nav || nav.organizations.length <= 1) return;
    const next = !this.orgMenuOpen();
    this.orgMenuOpen.set(next);
    if (next) {
      this.accountMenuOpen.set(false);
      this.activityMenuOpen.set(false);
    }
  }

  protected onOrgSwitcherClick(): void {
    if ((this.navigation()?.organizations?.length ?? 0) > 1) this.toggleOrgMenu();
  }

  protected closeOrgMenu(): void {
    this.orgMenuOpen.set(false);
  }

  protected getInitials(name: string): string {
    if (!name) return '?';
    return name
      .split(' ')
      .map(n => n[0])
      .filter((_, i) => i < 2)
      .join('')
      .toUpperCase();
  }

  protected navigateTo(path: string): void {
    void this.router.navigate([path]);
  }

  protected logout(): void {
    this.closeAccountMenu();
    void this.auth.logout();
  }

  @HostListener('document:click', ['$event'])
  protected onDocumentClick(ev: MouseEvent): void {
    const t = ev.target as Node;
    if (this.accountMenuOpen()) {
      const dropdown = this.accountDropdown()?.nativeElement;
      if (dropdown && !dropdown.contains(t)) this.accountMenuOpen.set(false);
    }
    if (this.activityMenuOpen()) {
      const panel = this.activityPanel()?.nativeElement;
      if (panel && !panel.contains(t)) this.activityMenuOpen.set(false);
    }
    if (this.orgMenuOpen()) {
      const sw = this.orgSwitcher()?.nativeElement;
      if (sw && !sw.contains(t)) this.orgMenuOpen.set(false);
    }
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    if (this.accountMenuOpen()) this.accountMenuOpen.set(false);
    if (this.activityMenuOpen()) this.activityMenuOpen.set(false);
    if (this.orgMenuOpen()) this.orgMenuOpen.set(false);
  }
}
