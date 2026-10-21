import { CommonModule } from '@angular/common';
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
import { debounceTime, distinctUntilChanged, from, of, Subject, switchMap } from 'rxjs';
import {
  AccountSettingsApi,
  NavigationApi,
  NavigationOrganizationDto,
  NavigationSummaryDto,
  NotificationDto,
  NotificationsApi,
  SearchApi,
  SearchHitDto,
  SearchResultDto
} from '../api';
import { AuthService } from '../auth/auth.service';
import { BrandingService } from '../services/branding.service';
import { SelectedOrgService } from '../services/selected-org.service';
import { ThemeService, type ThemeId, parseThemeId } from '../theme/theme.service';

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
  protected readonly branding = inject(BrandingService);
  protected readonly selectedOrg = inject(SelectedOrgService);
  private readonly router = inject(Router);
  private readonly navigationApi = inject(NavigationApi);
  private readonly accountSettingsApi = inject(AccountSettingsApi);
  private readonly notificationsApi = inject(NotificationsApi);
  private readonly searchApi = inject(SearchApi);

  private readonly accountDropdown = viewChild<ElementRef<HTMLElement>>('accountDropdown');
  private readonly activityPanel = viewChild<ElementRef<HTMLElement>>('activityPanel');
  private readonly notificationsPanel = viewChild<ElementRef<HTMLElement>>('notificationsPanel');
  private readonly themePanel = viewChild<ElementRef<HTMLElement>>('themePanel');
  private readonly orgSwitcher = viewChild<ElementRef<HTMLElement>>('orgSwitcher');
  private readonly searchPanel = viewChild<ElementRef<HTMLElement>>('searchPanel');
  private readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('searchInput');

  protected readonly sidebarMode = signal<'expanded' | 'icon'>('expanded');
  protected readonly accountMenuOpen = signal(false);
  protected readonly activityMenuOpen = signal(false);
  protected readonly notificationsMenuOpen = signal(false);
  protected readonly themeMenuOpen = signal(false);
  protected readonly orgMenuOpen = signal(false);
  protected readonly searchOpen = signal(false);
  protected readonly searchQuery = signal('');
  protected readonly searchHits = signal<SearchHitDto[]>([]);
  protected readonly searchBusy = signal(false);

  protected readonly navigation = signal<NavigationSummaryDto | null>(null);
  protected readonly navigationError = signal<string | null>(null);
  protected readonly notifications = signal<NotificationDto[]>([]);
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
            return of<SearchResultDto>({ hits: [] });
          }
          return from(
            this.searchApi.searchSearch({
              q: term,
              organizationId: this.selectedOrg.selectedOrgId() ?? undefined
            })
          );
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
      const res = await this.navigationApi.navigationGet();
      this.navigation.set(res);
      this.applyStoredOrgSelection();
    } catch {
      this.navigation.set(null);
      this.navigationError.set('Could not load navigation.');
    }
  }

  private async applyAccountPreferences(): Promise<void> {
    try {
      const settings = await this.accountSettingsApi.accountSettingsGetSettings();
      const preferred = parseThemeId(settings.preferredTheme);
      if (preferred) this.theme.setTheme(preferred);
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

  protected selectedOrganization(): NavigationOrganizationDto | null {
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

  protected toggleThemeMenu(): void {
    const next = !this.themeMenuOpen();
    this.themeMenuOpen.set(next);
    if (next) this.closeOtherPanels('theme');
  }

  protected selectTheme(id: ThemeId): void {
    this.theme.setTheme(id);
    this.themeMenuOpen.set(false);
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
      const list = await this.notificationsApi.notificationsList();
      this.notifications.set(list ?? []);
      await this.loadNavigation();
    } catch {
      this.notifications.set([]);
    } finally {
      this.notificationsLoading.set(false);
    }
  }

  protected async markNotificationRead(n: NotificationDto, ev: Event): Promise<void> {
    ev.preventDefault();
    ev.stopPropagation();
    if (n.isRead) return;
    try {
      await this.notificationsApi.notificationsMarkRead({ id: n.id });
      await this.loadNotifications();
    } catch {
      /* ignore */
    }
  }

  protected async markAllNotificationsRead(): Promise<void> {
    try {
      await this.notificationsApi.notificationsMarkAllRead();
      await this.loadNotifications();
    } catch {
      /* ignore */
    }
  }

  protected openNotification(n: NotificationDto): void {
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

  protected openSearchHit(hit: SearchHitDto): void {
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

  private closeOtherPanels(
    except: 'account' | 'activity' | 'notifications' | 'theme' | 'org' | 'search'
  ): void {
    if (except !== 'account') this.accountMenuOpen.set(false);
    if (except !== 'activity') this.activityMenuOpen.set(false);
    if (except !== 'notifications') this.notificationsMenuOpen.set(false);
    if (except !== 'theme') this.themeMenuOpen.set(false);
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
    if (this.themeMenuOpen()) {
      const panel = this.themePanel()?.nativeElement;
      if (panel && !panel.contains(t)) this.themeMenuOpen.set(false);
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
    if (this.themeMenuOpen()) this.themeMenuOpen.set(false);
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
