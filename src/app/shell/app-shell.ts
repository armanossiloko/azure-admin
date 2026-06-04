import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import {
  Component,
  ElementRef,
  HostListener,
  OnInit,
  inject,
  signal,
  viewChild
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { debounceTime, distinctUntilChanged, of, Subject, switchMap } from 'rxjs';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { SelectedOrgService } from '../services/selected-org.service';
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

type NotificationItem = {
  id: string;
  kind: string;
  title: string;
  body: string | null;
  href: string | null;
  createdAt: string;
  isRead: boolean;
};

type SearchHit = {
  kind: string;
  title: string;
  subtitle: string | null;
  href: string;
};

type AccountSettings = {
  defaultOrganizationId: string | null;
  preferredTheme: string | null;
};

@Component({
  standalone: true,
  selector: 'app-shell',
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive, FormsModule],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss'
})
export class AppShell implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);
  protected readonly selectedOrg = inject(SelectedOrgService);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);

  private readonly accountDropdown = viewChild<ElementRef<HTMLElement>>('accountDropdown');
  private readonly activityPanel = viewChild<ElementRef<HTMLElement>>('activityPanel');
  private readonly notificationsPanel = viewChild<ElementRef<HTMLElement>>('notificationsPanel');
  private readonly orgSwitcher = viewChild<ElementRef<HTMLElement>>('orgSwitcher');
  private readonly searchPanel = viewChild<ElementRef<HTMLElement>>('searchPanel');
  private readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('searchInput');

  protected readonly sidebarMode = signal<'expanded' | 'icon'>('expanded');
  protected readonly accountMenuOpen = signal(false);
  protected readonly activityMenuOpen = signal(false);
  protected readonly notificationsMenuOpen = signal(false);
  protected readonly orgMenuOpen = signal(false);
  protected readonly searchOpen = signal(false);
  protected readonly searchQuery = signal('');
  protected readonly searchHits = signal<SearchHit[]>([]);
  protected readonly searchBusy = signal(false);

  protected readonly navigation = signal<NavigationSummary | null>(null);
  protected readonly navigationError = signal<string | null>(null);
  protected readonly notifications = signal<NotificationItem[]>([]);
  protected readonly notificationsLoading = signal(false);

  private readonly searchTerms = new Subject<string>();

  async ngOnInit(): Promise<void> {
    this.searchTerms
      .pipe(
        debounceTime(250),
        distinctUntilChanged(),
        switchMap(q => {
          this.searchBusy.set(true);
          const term = q.trim();
          if (term.length < 2) {
            this.searchHits.set([]);
            this.searchBusy.set(false);
            return of({ hits: [] as SearchHit[] });
          }
          return this.http.get<{ hits: SearchHit[] }>('/api/search', { params: { q: term } });
        })
      )
      .subscribe({
        next: res => {
          this.searchHits.set(res?.hits ?? []);
          this.searchBusy.set(false);
        },
        error: () => {
          this.searchHits.set([]);
          this.searchBusy.set(false);
        }
      });

    await this.loadNavigation();
    await this.applyAccountPreferences();
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

  private async applyAccountPreferences(): Promise<void> {
    try {
      const settings = await firstValueFrom(
        this.http.get<AccountSettings>('/api/account/settings')
      );
      if (settings.preferredTheme === 'light' || settings.preferredTheme === 'dark') {
        this.theme.setTheme(settings.preferredTheme);
      }
      const nav = this.navigation();
      if (!nav?.organizations.length) return;
      const stored = this.selectedOrg.readStored();
      if (stored && nav.organizations.some(o => o.id === stored)) return;
      if (settings.defaultOrganizationId &&
          nav.organizations.some(o => o.id === settings.defaultOrganizationId)) {
        this.selectOrganization(settings.defaultOrganizationId);
      }
    } catch {
      /* optional */
    }
  }

  private applyStoredOrgSelection(): void {
    const nav = this.navigation();
    if (!nav?.organizations.length) {
      this.selectedOrg.select(null);
      return;
    }
    const stored = this.selectedOrg.readStored();
    const match = stored && nav.organizations.some(o => o.id === stored);
    const id = match ? stored : nav.organizations[0].id;
    this.selectedOrg.select(id);
  }

  protected selectedOrganization(): NavigationOrganization | null {
    const nav = this.navigation();
    if (!nav?.organizations.length) return null;
    const id = this.selectedOrg.selectedOrgId();
    if (id) {
      const found = nav.organizations.find(o => o.id === id);
      if (found) return found;
    }
    return nav.organizations[0];
  }

  protected selectOrganization(id: string): void {
    this.selectedOrg.select(id);
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
    if (next) this.closeOtherPanels('account');
  }

  protected closeAccountMenu(): void {
    this.accountMenuOpen.set(false);
  }

  protected toggleActivityMenu(): void {
    const next = !this.activityMenuOpen();
    this.activityMenuOpen.set(next);
    if (next) this.closeOtherPanels('activity');
  }

  protected closeActivityMenu(): void {
    this.activityMenuOpen.set(false);
  }

  protected async toggleNotificationsMenu(): Promise<void> {
    const next = !this.notificationsMenuOpen();
    this.notificationsMenuOpen.set(next);
    if (next) {
      this.closeOtherPanels('notifications');
      await this.loadNotifications();
    }
  }

  protected closeNotificationsMenu(): void {
    this.notificationsMenuOpen.set(false);
  }

  protected async loadNotifications(): Promise<void> {
    this.notificationsLoading.set(true);
    try {
      const list = await firstValueFrom(
        this.http.get<NotificationItem[]>('/api/notifications')
      );
      this.notifications.set(list ?? []);
      await this.loadNavigation();
    } catch {
      this.notifications.set([]);
    } finally {
      this.notificationsLoading.set(false);
    }
  }

  protected async markNotificationRead(n: NotificationItem, ev: Event): Promise<void> {
    ev.preventDefault();
    ev.stopPropagation();
    if (n.isRead) return;
    try {
      await firstValueFrom(this.http.post(`/api/notifications/${n.id}/read`, {}));
      await this.loadNotifications();
    } catch {
      /* ignore */
    }
  }

  protected async markAllNotificationsRead(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/notifications/read-all', {}));
      await this.loadNotifications();
    } catch {
      /* ignore */
    }
  }

  protected openNotification(n: NotificationItem): void {
    void this.markNotificationRead(n, new Event('click'));
    this.closeNotificationsMenu();
    if (n.href) void this.router.navigateByUrl(n.href);
  }

  protected onSearchInput(value: string): void {
    this.searchQuery.set(value);
    this.searchOpen.set(true);
    this.searchTerms.next(value);
  }

  protected focusSearch(): void {
    this.searchInput()?.nativeElement.focus();
    this.searchOpen.set(true);
  }

  protected closeSearch(): void {
    this.searchOpen.set(false);
  }

  protected openSearchHit(hit: SearchHit): void {
    this.closeSearch();
    this.searchQuery.set('');
    this.searchHits.set([]);
    void this.router.navigateByUrl(hit.href);
  }

  protected toggleOrgMenu(): void {
    const nav = this.navigation();
    if (!nav || nav.organizations.length <= 1) return;
    const next = !this.orgMenuOpen();
    this.orgMenuOpen.set(next);
    if (next) this.closeOtherPanels('org');
  }

  protected onOrgSwitcherClick(): void {
    if ((this.navigation()?.organizations?.length ?? 0) > 1) this.toggleOrgMenu();
  }

  protected closeOrgMenu(): void {
    this.orgMenuOpen.set(false);
  }

  private closeOtherPanels(except: 'account' | 'activity' | 'notifications' | 'org' | 'search'): void {
    if (except !== 'account') this.accountMenuOpen.set(false);
    if (except !== 'activity') this.activityMenuOpen.set(false);
    if (except !== 'notifications') this.notificationsMenuOpen.set(false);
    if (except !== 'org') this.orgMenuOpen.set(false);
    if (except !== 'search') this.searchOpen.set(false);
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
    if (this.notificationsMenuOpen()) {
      const panel = this.notificationsPanel()?.nativeElement;
      if (panel && !panel.contains(t)) this.notificationsMenuOpen.set(false);
    }
    if (this.orgMenuOpen()) {
      const sw = this.orgSwitcher()?.nativeElement;
      if (sw && !sw.contains(t)) this.orgMenuOpen.set(false);
    }
    if (this.searchOpen()) {
      const sp = this.searchPanel()?.nativeElement;
      if (sp && !sp.contains(t)) this.closeSearch();
    }
  }

  @HostListener('document:keydown', ['$event'])
  protected onDocumentKeydown(ev: KeyboardEvent): void {
    if (ev.key === 'Escape') {
      this.onEscape();
      return;
    }
    if (ev.key === '/' && !this.isTypingInField(ev.target)) {
      ev.preventDefault();
      this.focusSearch();
    }
    if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'k') {
      ev.preventDefault();
      this.focusSearch();
    }
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    if (this.accountMenuOpen()) this.accountMenuOpen.set(false);
    if (this.activityMenuOpen()) this.activityMenuOpen.set(false);
    if (this.notificationsMenuOpen()) this.notificationsMenuOpen.set(false);
    if (this.orgMenuOpen()) this.orgMenuOpen.set(false);
    if (this.searchOpen()) this.closeSearch();
  }

  private isTypingInField(target: EventTarget | null): boolean {
    const el = target as HTMLElement | null;
    if (!el) return false;
    const tag = el.tagName?.toLowerCase();
    return tag === 'input' || tag === 'textarea' || el.isContentEditable;
  }
}
