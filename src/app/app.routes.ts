import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
import { guestGuard } from './auth/guest.guard';
import { AppShell } from './shell/app-shell';
import { AzureOrganizationDetailPage } from './pages/azure-organizations/azure-organization-detail.page';
import { AzureOrganizationsPage } from './pages/azure-organizations/azure-organizations.page';
import { DashboardPage } from './pages/dashboard/dashboard.page';
import { LoginPage } from './pages/login/login.page';
import { ReleaseCreatePage } from './pages/release-create/release-create.page';
import { ReleaseDetailPage } from './pages/releases/release-detail.page';
import { ReleaseListPage } from './pages/releases/release-list.page';
import { RepositoriesPage } from './pages/repositories/repositories.page';
import { AccountSettingsPage } from './pages/account/account-settings.page';
import { SettingsPage } from './pages/settings/settings.page';
import { TeamsPage } from './pages/teams/teams.page';
import { BranchesPage } from './pages/branches/branches.page';

export const routes: Routes = [
  { path: 'login', component: LoginPage, canActivate: [guestGuard] },
  {
    path: '',
    component: AppShell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: '/dashboard' },
      { path: 'dashboard', component: DashboardPage },
      { path: 'releases/new', component: ReleaseCreatePage },
      { path: 'releases/:releaseId/add-prs', component: ReleaseCreatePage },
      { path: 'releases/:id', component: ReleaseDetailPage },
      { path: 'releases', component: ReleaseListPage },
      { path: 'teams', component: TeamsPage },
      { path: 'repositories', component: RepositoriesPage },
      { path: 'branches', component: BranchesPage },
      { path: 'settings/account', component: AccountSettingsPage },
      { path: 'settings', component: SettingsPage },
      { path: 'settings/azure-organizations', component: AzureOrganizationsPage },
      { path: 'settings/azure-organizations/:orgId', component: AzureOrganizationDetailPage },
      { path: 'settings/pat-credentials', redirectTo: '/settings/azure-organizations', pathMatch: 'full' }
    ]
  }
];
