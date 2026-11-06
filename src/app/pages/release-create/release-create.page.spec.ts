import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { RegisteredRepositoriesApi, ReleasesApi, TeamsApi } from '../../api';
import { SelectedOrgService } from '../../services/selected-org.service';
import { SettingsService } from '../../services/settings.service';
import { ReleaseCreatePage } from './release-create.page';

/** The protected members the template drives, exposed for the test. */
type PageUnderTest = {
  ngOnInit(): Promise<void>;
  submit(): Promise<void>;
  toggleTeamIncluded(teamId: string, checked: boolean): Promise<void>;
  toggleRepo(teamId: string, repoId: string): void;
  error: () => string | null;
};

describe('ReleaseCreatePage link to the release', () => {
  let fixture: ComponentFixture<ReleaseCreatePage>;
  let page: PageUnderTest;
  let releases: {
    releasesGet: ReturnType<typeof vi.fn>;
    releasesFindOrCreateRelease: ReturnType<typeof vi.fn>;
    releasesPreflightPullRequests: ReturnType<typeof vi.fn>;
    releasesBatchCreatePullRequestsForTeams: ReturnType<typeof vi.fn>;
  };

  async function open(routeParams: Record<string, string> = {}): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [ReleaseCreatePage],
      providers: [
        provideRouter([]),
        { provide: ReleasesApi, useValue: releases },
        { provide: TeamsApi, useValue: { teamsList: async () => [{ id: 't1', name: 'Team' }] } },
        {
          provide: RegisteredRepositoriesApi,
          useValue: { registeredRepositoriesList: async () => [{ id: 'repo1', isDecommissioned: false }] }
        },
        { provide: SelectedOrgService, useValue: { selectedOrgId: () => null } },
        { provide: SettingsService, useValue: { getSettings: async () => ({ sprintWeeks: 0 }) } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(routeParams) } } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ReleaseCreatePage);
    page = fixture.componentInstance as unknown as PageUnderTest;
    await page.ngOnInit();
    await page.toggleTeamIncluded('t1', true);
    page.toggleRepo('t1', 'repo1');
  }

  function viewReleaseHref(): string | null {
    fixture.detectChanges();
    const links = Array.from<HTMLAnchorElement>(fixture.nativeElement.querySelectorAll('a.btn'));
    return links.find(a => a.textContent?.trim() === 'View release')?.getAttribute('href') ?? null;
  }

  beforeEach(() => {
    releases = {
      releasesGet: vi.fn(async () => ({ id: 'existing', title: 'Existing', sprintLabel: '2026/40', status: 'Active' })),
      releasesFindOrCreateRelease: vi.fn(async () => ({ id: 'new-release' })),
      releasesPreflightPullRequests: vi.fn(async () => ({ repositories: [{ ok: true }] })),
      releasesBatchCreatePullRequestsForTeams: vi.fn(async () => ({ results: [] }))
    };
  });

  it('is absent until the form is submitted', async () => {
    await open();

    expect(viewReleaseHref()).toBeNull();
  });

  it('links to the release once it is created', async () => {
    await open();

    await page.submit();

    expect(viewReleaseHref()).toBe('/releases/new-release');
  });

  it('links to the release when PRs were added to an existing one', async () => {
    await open({ releaseId: 'existing' });

    await page.submit();

    expect(viewReleaseHref()).toBe('/releases/existing');
    expect(releases.releasesFindOrCreateRelease).not.toHaveBeenCalled();
  });

  it('still links to the release when preflight stops its PRs from opening', async () => {
    releases.releasesPreflightPullRequests.mockResolvedValue({
      repositories: [{ ok: false, serviceName: 'svc', issues: ['Branch is missing'] }]
    });
    await open();

    await page.submit();

    expect(page.error()).toContain('Preflight');
    expect(viewReleaseHref()).toBe('/releases/new-release');
  });

  it('is absent when the release could not be created', async () => {
    releases.releasesFindOrCreateRelease.mockRejectedValue(new Error('boom'));
    await open();

    await page.submit();

    expect(viewReleaseHref()).toBeNull();
  });
});
