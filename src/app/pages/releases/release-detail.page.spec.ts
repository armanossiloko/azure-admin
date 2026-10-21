import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { ReleaseDetailDto, ReleaseLifecycleStatus, ReleasesApi } from '../../api';
import { ReleaseDetailPage } from './release-detail.page';

function release(status: ReleaseLifecycleStatus, concurrencyStamp: string, title = 'Release sprint 2026/42'): ReleaseDetailDto {
  return {
    id: 'r1',
    title,
    sprintLabel: '2026/42',
    status,
    createdAt: '2026-10-21T10:00:00Z',
    concurrencyStamp,
    teams: [],
    pullRequests: [],
    repositoryCommitNotes: []
  };
}

/** The protected members the template drives, exposed for the test. */
type PageUnderTest = {
  ngOnInit(): Promise<void>;
  closeRelease(): Promise<void>;
  archiveRelease(): Promise<void>;
  startEditMeta(): void;
  saveMeta(): Promise<void>;
  editTitle: { (): string; set(v: string): void };
  editStatus: () => ReleaseLifecycleStatus;
  error: () => string | null;
  editingMeta: () => boolean;
};

describe('ReleaseDetailPage concurrency', () => {
  let stored: ReleaseDetailDto;
  let api: {
    releasesGet: ReturnType<typeof vi.fn>;
    releasesClose: ReturnType<typeof vi.fn>;
    releasesUpdate: ReturnType<typeof vi.fn>;
    releasesRefreshPullRequestStatuses: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    stored = release('Active', 'stamp-1');
    api = {
      releasesGet: vi.fn(async () => structuredClone(stored)),
      releasesClose: vi.fn(async () => {
        stored = release('Completed', 'stamp-2');
        return stored;
      }),
      releasesUpdate: vi.fn(async () => stored),
      releasesRefreshPullRequestStatuses: vi.fn(async () => ({ results: [] }))
    };
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    await TestBed.configureTestingModule({
      imports: [ReleaseDetailPage],
      providers: [
        provideRouter([]),
        { provide: ReleasesApi, useValue: api },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'r1' }) } } }
      ]
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  async function open(): Promise<PageUnderTest> {
    const page = TestBed.createComponent(ReleaseDetailPage).componentInstance as unknown as PageUnderTest;
    await page.ngOnInit();
    return page;
  }

  it('archives with the stamp issued by Close, not the one loaded before it', async () => {
    const page = await open();

    await page.closeRelease();
    await page.archiveRelease();

    expect(api.releasesClose).toHaveBeenCalledWith({ id: 'r1', concurrencyStamp: 'stamp-1' });
    expect(api.releasesUpdate).toHaveBeenCalledWith({
      id: 'r1',
      body: { status: 'Archived', concurrencyStamp: 'stamp-2' }
    });
    expect(page.error()).toBeNull();
  });

  it('refills the edit form with the other user\'s version after a save conflict', async () => {
    const page = await open();
    page.startEditMeta();
    page.editTitle.set('My title');

    // Meanwhile someone else archived and renamed the release.
    stored = release('Archived', 'stamp-9', 'Their title');
    api.releasesUpdate.mockRejectedValueOnce(
      new HttpErrorResponse({ status: 409, error: { message: 'changed by someone else' } })
    );

    await page.saveMeta();

    expect(api.releasesUpdate).toHaveBeenCalledWith({
      id: 'r1',
      body: { title: 'My title', sprintLabel: '2026/42', concurrencyStamp: 'stamp-1' }
    });
    expect(page.editingMeta()).toBe(true);
    expect(page.editTitle()).toBe('Their title');
    // A status that is still a valid choice for an archived release, so the next save cannot 400.
    expect(page.editStatus()).toBe('Archived');
    expect(page.error()).toContain('The form now shows their version');

    // Saving again sends the fresh stamp.
    api.releasesUpdate.mockResolvedValueOnce(stored);
    await page.saveMeta();
    expect(api.releasesUpdate).toHaveBeenLastCalledWith({
      id: 'r1',
      body: { title: 'Their title', sprintLabel: '2026/42', concurrencyStamp: 'stamp-9' }
    });
  });
});
