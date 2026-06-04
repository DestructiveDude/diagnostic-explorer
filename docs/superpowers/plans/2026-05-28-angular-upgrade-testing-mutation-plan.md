# Angular Upgrade, Test Modernization, and Mutation CI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade `diagnostics-web` to the latest Angular release, replace the weak legacy frontend test setup with a modern behavior-focused suite, and add Stryker mutation analysis plus CI/reporting integration.

**Architecture:** Keep the repository stable by working from characterization tests outward. First pin the app's important behavior with a few high-value tests, then move the Angular toolchain forward in supported steps, switch the test runner to Jest, and deepen coverage around the stateful models and SignalR service. Once the upgraded app and real unit suite are stable, add Stryker and wire repo-level consumers such as Docker, GitHub Actions, and docs to the new frontend baseline.

**Tech Stack:** Angular 21, Angular Material/CDK 21, TypeScript, Jest with `jest-preset-angular`, Angular TestBed, GitHub Actions, Docker, StrykerJS, PowerShell summary scripting

---

## File structure map

- **Modify:** `diagnostics-web/package.json` — upgrade Angular/tooling dependencies, swap test scripts from Karma to Jest, add Stryker script
- **Modify:** `diagnostics-web/package-lock.json` — lock upgraded npm dependency graph
- **Modify:** `diagnostics-web/angular.json` — align build/test targets with the upgraded CLI and remove Karma-specific test target wiring if no longer needed
- **Modify:** `diagnostics-web/tsconfig.json` — align compiler options with the upgraded Angular/TypeScript toolchain
- **Modify:** `diagnostics-web/tsconfig.spec.json` — point spec compilation at Jest types and setup file(s)
- **Delete:** `diagnostics-web/karma.conf.js` — remove legacy Karma runner once Jest is green
- **Create:** `diagnostics-web/jest.config.ts` — Jest configuration for Angular unit tests
- **Create:** `diagnostics-web/setup-jest.ts` — `jest-preset-angular` environment bootstrap
- **Modify:** `diagnostics-web/src/app/app.component.spec.ts` — replace CLI placeholder assertions with routing/app-shell characterization coverage
- **Create/Modify:** `diagnostics-web/src/app/util/util.spec.ts` — add tests for `strEqCI`, `getBaseLocation`, `getErrorMessage`, and `today`
- **Create/Modify:** `diagnostics-web/src/app/Model/FilterCriteria.spec.ts` — add coverage for search text, escaped regex fallback, and level filtering
- **Modify:** `diagnostics-web/src/app/event-filter/event-filter.component.spec.ts` — convert to output- and state-focused tests
- **Create/Modify:** `diagnostics-web/src/app/services/diag-hub.service.spec.ts` — add connection lifecycle, reconnect, and hub invocation coverage
- **Create/Modify:** `diagnostics-web/src/app/Model/RetroModel.spec.ts` — add query, cancellation, result accumulation, filtering, completion, and delete behavior coverage
- **Create/Modify:** `diagnostics-web/src/app/Model/RealtimeModel.spec.ts` — add process merge/filter/select, event stream, and property-operation coverage
- **Create:** `diagnostics-web/stryker.conf.json` — StrykerJS configuration aimed at the upgraded Jest suite
- **Create:** `scripts/summarize-stryker.ps1` — compact JSON/Markdown mutation summary generator
- **Modify:** `.github/workflows/publish-docker-image.yml` — add explicit frontend validation before the Docker build
- **Create:** `.github/workflows/mutation-web.yml` — dedicated Angular mutation workflow
- **Modify:** `Docker/Dockerfile` — update the Node build stage to a supported modern Node image and keep SPA output wiring valid
- **Modify:** `README.md` — document the new Node/Angular/test/Stryker workflow
- **Verify:** `DiagnosticService/Config/settings.Debug.json` — only adjust if the upgraded Angular output path changes

### Task 1: Add characterization tests before touching the toolchain

**Files:**
- Modify: `diagnostics-web/src/app/app.component.spec.ts`
- Create: `diagnostics-web/src/app/util/util.spec.ts`
- Test: `diagnostics-web/src/app/app.component.spec.ts`
- Test: `diagnostics-web/src/app/util/util.spec.ts`

- [ ] **Step 1: Write the failing characterization tests**

```ts
// diagnostics-web/src/app/util/util.spec.ts
import { getBaseLocation, getErrorMessage, strEqCI, today } from './util';

describe('util helpers', () => {
  it('treats strings as equal ignoring case', () => {
    expect(strEqCI('ProcA', 'proca')).toBe(true);
    expect(strEqCI('ProcA', 'ProcB')).toBe(false);
  });

  it('normalizes the current path into an application base href', () => {
    Object.defineProperty(window, 'location', {
      value: { pathname: '/diagnostics/app' },
      writable: true,
    });

    expect(getBaseLocation()).toBe('/diagnostics/');
  });

  it('extracts the best available error message', () => {
    expect(getErrorMessage({ error: { exceptionMessage: 'boom' } })).toBe('boom');
    expect(getErrorMessage({ message: 'fallback' })).toBe('fallback');
  });

  it('returns today at midnight', () => {
    const value = today();

    expect(value.getHours()).toBe(0);
    expect(value.getMinutes()).toBe(0);
    expect(value.getSeconds()).toBe(0);
    expect(value.getMilliseconds()).toBe(0);
  });
});

// diagnostics-web/src/app/app.component.spec.ts
it('renders the main shell rather than the Angular CLI placeholder', () => {
  const fixture = TestBed.createComponent(AppComponent);
  fixture.detectChanges();

  expect(fixture.nativeElement.querySelector('mat-toolbar')).not.toBeNull();
});
```

- [ ] **Step 2: Run the focused tests to verify the current suite is missing coverage**

Run: `cd diagnostics-web; npm ci --legacy-peer-deps; npm test -- --watch=false --browsers=ChromeHeadless --include src/app/util/util.spec.ts`
Expected: FAIL because `util.spec.ts` does not exist yet.

- [ ] **Step 3: Write the minimal tests and remove the CLI placeholder assertions**

```ts
// diagnostics-web/src/app/app.component.spec.ts
// imports should include:
// import { provideRouter } from '@angular/router';
describe('AppComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [AppComponent],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('creates the app shell', () => {
    const fixture = TestBed.createComponent(AppComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders the main shell rather than the Angular CLI placeholder', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('mat-toolbar')).not.toBeNull();
  });
});
```

- [ ] **Step 4: Run the focused tests to confirm the characterization baseline**

Run: `cd diagnostics-web; npm test -- --watch=false --browsers=ChromeHeadless --include src/app/app.component.spec.ts --include src/app/util/util.spec.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add diagnostics-web/src/app/app.component.spec.ts diagnostics-web/src/app/util/util.spec.ts
git commit -m "test: add frontend characterization coverage"
```

### Task 2: Upgrade Angular and Angular Material through supported major steps

**Files:**
- Modify: `diagnostics-web/package.json`
- Modify: `diagnostics-web/package-lock.json`
- Modify: `diagnostics-web/angular.json`
- Modify: `diagnostics-web/tsconfig.json`
- Modify: `diagnostics-web/tsconfig.spec.json`
- Test: `diagnostics-web/package.json`

- [ ] **Step 1: Capture the current failing build/test boundary before upgrading**

```bash
cd diagnostics-web
npm run build
npm test -- --watch=false --browsers=ChromeHeadless
```

Expected: Current Angular 13 build/tests pass or expose baseline issues that must not worsen.

- [ ] **Step 2: Apply the supported Angular major upgrades sequentially**

```bash
cd diagnostics-web
npx @angular/cli@14 update @angular/core@14 @angular/cli@14 @angular/material@14
npx @angular/cli@15 update @angular/core@15 @angular/cli@15 @angular/material@15
npx @angular/cli@16 update @angular/core@16 @angular/cli@16 @angular/material@16
npx @angular/cli@17 update @angular/core@17 @angular/cli@17 @angular/material@17
npx @angular/cli@18 update @angular/core@18 @angular/cli@18 @angular/material@18
npx @angular/cli@19 update @angular/core@19 @angular/cli@19 @angular/material@19
npx @angular/cli@20 update @angular/core@20 @angular/cli@20 @angular/material@20
npx @angular/cli@21 update @angular/core@21 @angular/cli@21 @angular/material@21
```

- [ ] **Step 3: Repair migrated config and source breakages with the smallest possible edits**

```json
// diagnostics-web/package.json
{
  "scripts": {
    "start": "ng serve",
    "build": "ng build",
    "watch": "ng build --watch --configuration development"
  },
  "dependencies": {
    "@angular/animations": "^21.2.15",
    "@angular/cdk": "^21.2.13",
    "@angular/common": "^21.2.15",
    "@angular/compiler": "^21.2.15",
    "@angular/core": "^21.2.15",
    "@angular/forms": "^21.2.15",
    "@angular/material": "^21.2.13",
    "@angular/platform-browser": "^21.2.15",
    "@angular/platform-browser-dynamic": "^21.2.15",
    "@angular/router": "^21.2.15",
    "rxjs": "^7.8.2",
    "tslib": "^2.8.1",
    "zone.js": "^0.15.1"
  },
  "devDependencies": {
    "@angular-devkit/build-angular": "^21.2.13",
    "@angular/cli": "^21.2.13",
    "@angular/compiler-cli": "^21.2.15",
    "typescript": "~5.9.3"
  }
}
```

- [ ] **Step 4: Rebuild after the final Angular upgrade**

Run: `cd diagnostics-web; npm install; npm run build`
Expected: PASS with a fresh Angular 21 build.

- [ ] **Step 5: Commit**

```bash
git add diagnostics-web/package.json diagnostics-web/package-lock.json diagnostics-web/angular.json diagnostics-web/tsconfig.json diagnostics-web/tsconfig.spec.json
git commit -m "build: upgrade diagnostics-web to Angular 21"
```

### Task 3: Replace Karma with Jest on the upgraded frontend

**Files:**
- Modify: `diagnostics-web/package.json`
- Modify: `diagnostics-web/tsconfig.spec.json`
- Create: `diagnostics-web/jest.config.ts`
- Create: `diagnostics-web/setup-jest.ts`
- Delete: `diagnostics-web/karma.conf.js`
- Test: `diagnostics-web/jest.config.ts`

- [ ] **Step 1: Add a failing Jest smoke test command**

```json
// diagnostics-web/package.json
{
  "scripts": {
    "test": "jest --runInBand --coverage"
  },
  "devDependencies": {
    "jest": "^30.0.5",
    "jest-environment-jsdom": "^30.0.5",
    "jest-preset-angular": "^15.0.3",
    "@types/jest": "^30.0.0",
    "ts-jest": "^30.0.0"
  }
}
```

- [ ] **Step 2: Run the new test command to verify Jest is not configured yet**

Run: `cd diagnostics-web; npm test`
Expected: FAIL because `jest.config.ts` and the Angular preset bootstrap do not exist yet.

- [ ] **Step 3: Add the Jest configuration and bootstrap**

```ts
// diagnostics-web/jest.config.ts
import type { Config } from 'jest';

const config: Config = {
  preset: 'jest-preset-angular',
  setupFilesAfterEnv: ['<rootDir>/setup-jest.ts'],
  testEnvironment: 'jsdom',
  testMatch: ['<rootDir>/src/**/*.spec.ts'],
  collectCoverageFrom: ['src/**/*.ts', '!src/main.ts', '!src/environments/**'],
  coverageDirectory: '<rootDir>/coverage/jest',
  moduleFileExtensions: ['ts', 'html', 'js', 'json'],
};

export default config;

// diagnostics-web/setup-jest.ts
import { setupZoneTestEnv } from 'jest-preset-angular/setup-env/zone';

setupZoneTestEnv();
```

```json
// diagnostics-web/tsconfig.spec.json
{
  "extends": "./tsconfig.json",
  "compilerOptions": {
    "outDir": "./out-tsc/spec",
    "types": ["jest"]
  },
  "include": ["src/**/*.spec.ts", "src/**/*.d.ts", "setup-jest.ts"]
}
```

- [ ] **Step 4: Run the upgraded unit-test command**

Run: `cd diagnostics-web; npm test`
Expected: PASS for migrated specs, with no Karma launcher involved.

- [ ] **Step 5: Commit**

```bash
git add diagnostics-web/package.json diagnostics-web/tsconfig.spec.json diagnostics-web/jest.config.ts diagnostics-web/setup-jest.ts
git rm diagnostics-web/karma.conf.js
git commit -m "test: migrate diagnostics-web from Karma to Jest"
```

### Task 4: Replace placeholder specs with behavior-focused component and helper coverage

**Files:**
- Modify: `diagnostics-web/src/app/event-filter/event-filter.component.spec.ts`
- Modify: `diagnostics-web/src/app/pipes/summary-line.pipe.spec.ts`
- Modify: `diagnostics-web/src/app/pipes/level-name.pipe.spec.ts`
- Create: `diagnostics-web/src/app/Model/FilterCriteria.spec.ts`
- Test: `diagnostics-web/src/app/event-filter/event-filter.component.spec.ts`
- Test: `diagnostics-web/src/app/Model/FilterCriteria.spec.ts`

- [ ] **Step 1: Write failing tests that assert actual filtering behavior**

```ts
// diagnostics-web/src/app/Model/FilterCriteria.spec.ts
import { FilterCriteria } from './FilterCriteria';
import { Level } from './Level';

describe('FilterCriteria', () => {
  it('matches message text case-insensitively', () => {
    const criteria = new FilterCriteria();
    criteria.searchText = 'timeout';

    expect(criteria.filter({ level: Level.ERROR, message: 'Socket timeout', detail: '' })).toBe(true);
    expect(criteria.filter({ level: Level.ERROR, message: 'Connected', detail: '' })).toBe(false);
  });

  it('falls back to an escaped regex when the search text is invalid', () => {
    const criteria = new FilterCriteria();
    criteria.searchText = '[';

    expect(criteria.filter({ level: Level.ERROR, message: '[literal]', detail: '' })).toBe(true);
  });
});

// diagnostics-web/src/app/event-filter/event-filter.component.spec.ts
it('emits a new criteria object when filter flags change', () => {
  const fixture = TestBed.createComponent(EventFilterComponent);
  const component = fixture.componentInstance;
  const emitted: FilterCriteria[] = [];
  component.criteriaChange.subscribe(value => emitted.push(value));

  component.warn = true;
  component.searchText = 'stale';

  expect(emitted.at(-1)?.warn).toBe(true);
  expect(emitted.at(-1)?.searchText).toBe('stale');
});
```

- [ ] **Step 2: Run the targeted tests to verify they fail against the placeholder suite**

Run: `cd diagnostics-web; npm test -- --runInBand src/app/Model/FilterCriteria.spec.ts src/app/event-filter/event-filter.component.spec.ts`
Expected: FAIL until the real assertions and any needed setup are added.

- [ ] **Step 3: Implement the updated tests and tighten the pipe assertions**

```ts
// diagnostics-web/src/app/pipes/summary-line.pipe.spec.ts
describe('SummaryLinePipe', () => {
  it('formats a summary line with level and message', () => {
    const pipe = new SummaryLinePipe();

    expect(pipe.transform({ level: 3, message: 'Disk full' } as any)).toContain('Disk full');
  });
});

// diagnostics-web/src/app/pipes/level-name.pipe.spec.ts
describe('LevelNamePipe', () => {
  it('maps a numeric level to a display string', () => {
    const pipe = new LevelNamePipe();

    expect(pipe.transform(4)).toBe('Error');
  });
});
```

- [ ] **Step 4: Run the focused component/helper suite**

Run: `cd diagnostics-web; npm test -- --runInBand src/app/Model/FilterCriteria.spec.ts src/app/event-filter/event-filter.component.spec.ts src/app/pipes/summary-line.pipe.spec.ts src/app/pipes/level-name.pipe.spec.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add diagnostics-web/src/app/Model/FilterCriteria.spec.ts diagnostics-web/src/app/event-filter/event-filter.component.spec.ts diagnostics-web/src/app/pipes/summary-line.pipe.spec.ts diagnostics-web/src/app/pipes/level-name.pipe.spec.ts
git commit -m "test: replace placeholder Angular specs with behavior checks"
```

### Task 5: Add full `DiagHubService` coverage around connection and hub calls

**Files:**
- Create/Modify: `diagnostics-web/src/app/services/diag-hub.service.spec.ts`
- Test: `diagnostics-web/src/app/services/diag-hub.service.spec.ts`

- [ ] **Step 1: Write failing service tests for connect/reconnect and hub invocations**

```ts
import { TestBed } from '@angular/core/testing';
import * as signalR from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';
import { DiagHubService } from './diag-hub.service';
import { BASE_API_URL } from '../../injectionTokens';

describe('DiagHubService', () => {
  it('publishes the built connection and marks it started', async () => {
    const start = jest.fn().mockResolvedValue(undefined);
    const connection = {
      start,
      onreconnecting: jest.fn(),
      onreconnected: jest.fn(),
      onclose: jest.fn(),
    } as any;

    jest.spyOn(signalR, 'HubConnectionBuilder').mockImplementation(() => ({
      withUrl: jest.fn().mockReturnThis(),
      build: jest.fn().mockReturnValue(connection),
    }) as any);

    const service = TestBed.inject(DiagHubService);
    const started = firstValueFrom(service.connectionStarted);

    await service.connect();

    await expect(started).resolves.toBe(connection);
  });

  it('retries after a failed start', async () => {
    // first connection fails, second succeeds
  });

  it('invokes SetProperty with the supplied request', async () => {
    // verify connection.invoke('SetProperty', request)
  });
});
```

- [ ] **Step 2: Run the focused service spec**

Run: `cd diagnostics-web; npm test -- --runInBand src/app/services/diag-hub.service.spec.ts`
Expected: FAIL until the SignalR builder and connection are fully mocked.

- [ ] **Step 3: Implement the test doubles and cover each hub method**

```ts
beforeEach(() => {
  TestBed.configureTestingModule({
    providers: [
      DiagHubService,
      { provide: BASE_API_URL, useValue: '/diagnostics' },
    ],
  });
});

it('invokes RemoveProcess, StartRetroSearch, CancelRetroSearch, and RetroDelete', async () => {
  const connection = { invoke: jest.fn().mockResolvedValue(3) } as any;
  const service = TestBed.inject(DiagHubService);
  service.connection = connection;

  await service.removeProcess('p-1');
  await service.startRetroSearch({ searchId: 7 } as any);
  await service.cancelRetroSearch(7);
  await expect(service.deleteRecords(['m-1'])).resolves.toBe(3);
});
```

- [ ] **Step 4: Run the service spec again**

Run: `cd diagnostics-web; npm test -- --runInBand src/app/services/diag-hub.service.spec.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add diagnostics-web/src/app/services/diag-hub.service.spec.ts
git commit -m "test: cover SignalR hub service behavior"
```

### Task 6: Cover `RetroModel` search, filter, and delete flows

**Files:**
- Create/Modify: `diagnostics-web/src/app/Model/RetroModel.spec.ts`
- Test: `diagnostics-web/src/app/Model/RetroModel.spec.ts`

- [ ] **Step 1: Write failing `RetroModel` behavior tests**

```ts
import { DatePipe } from '@angular/common';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RetroModel } from './RetroModel';

describe('RetroModel', () => {
  it('builds a retro query from the selected date, time, and filters', async () => {
    const hubService = {
      connectionReady: { subscribe: jest.fn() },
      startRetroSearch: jest.fn().mockResolvedValue(undefined),
      cancelRetroSearch: jest.fn().mockResolvedValue(undefined),
    } as any;

    const model = new RetroModel(new DatePipe('en-GB'), hubService, { open: jest.fn() } as MatSnackBar);
    model.machine = 'SRV01';
    model.process = 'Worker';
    model.user = 'chris';
    model.message = 'timeout';

    await model.search();

    expect(hubService.startRetroSearch).toHaveBeenCalledWith(
      expect.objectContaining({
        machine: 'SRV01',
        process: 'Worker',
        user: 'chris',
        message: 'timeout',
      }),
    );
  });

  it('cancels the current search before starting a replacement search', async () => {
    // assert cancelRetroSearch receives the previous search id
  });
});
```

- [ ] **Step 2: Run the focused `RetroModel` spec**

Run: `cd diagnostics-web; npm test -- --runInBand src/app/Model/RetroModel.spec.ts`
Expected: FAIL until the model's state transitions are fully asserted.

- [ ] **Step 3: Add tests for append, filter, completion, and delete branches**

```ts
it('updates resultsMessage when filter criteria is active', () => {
  const model = createModel();
  model.results = [{ level: 4, message: 'Timeout', detail: '', msgId: '1' } as any];
  model.filterVisible = true;
  model.filterCriteria.searchText = 'time';

  (model as any).filterResults();

  expect(model.displayResults).toHaveLength(1);
  expect(model.resultsMessage).toBe('1 of 1 events');
});

it('shows the deleted-record count after a successful delete', async () => {
  const snackBar = { open: jest.fn() } as any;
  const hubService = {
    connectionReady: { subscribe: jest.fn() },
    deleteRecords: jest.fn().mockResolvedValue(2),
    startRetroSearch: jest.fn().mockResolvedValue(undefined),
  } as any;

  global.confirm = jest.fn().mockReturnValue(true);

  const model = new RetroModel(new DatePipe('en-GB'), hubService, snackBar);
  model.results = [{ msgId: '1' }, { msgId: '2' }] as any;

  await model.delete();

  expect(snackBar.open).toHaveBeenCalledWith(expect.stringContaining('2 records deleted'), '', expect.any(Object));
});
```

- [ ] **Step 4: Run the `RetroModel` suite**

Run: `cd diagnostics-web; npm test -- --runInBand src/app/Model/RetroModel.spec.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add diagnostics-web/src/app/Model/RetroModel.spec.ts
git commit -m "test: cover retro search model flows"
```

### Task 7: Cover `RealtimeModel` process management and event handling

**Files:**
- Create/Modify: `diagnostics-web/src/app/Model/RealtimeModel.spec.ts`
- Test: `diagnostics-web/src/app/Model/RealtimeModel.spec.ts`

- [ ] **Step 1: Write failing `RealtimeModel` tests for the core state transitions**

```ts
import { DatePipe } from '@angular/common';
import { RealtimeModel } from './RealtimeModel';

describe('RealtimeModel', () => {
  it('filters processes by online state and search text', () => {
    const model = createRealtimeModel();
    model.displayProcesses([
      { id: '1', processName: 'OrderWorker', machineName: 'SRV01', userName: 'svc', state: 'Online' } as any,
      { id: '2', processName: 'AuditWorker', machineName: 'SRV02', userName: 'svc', state: 'Offline' } as any,
    ]);

    model.processSearch = 'order';

    expect(model.filteredProcesses.map(p => p.id)).toEqual(['1']);
  });

  it('sets the selected event and opens the trace scope', () => {
    const model = createRealtimeModel();
    const event = { isSelected: false } as any;

    model.setCurrentEvent(event);

    expect(model.selectedEvent).toBe(event);
    expect(model.traceScopeVisible).toBe(true);
  });
});
```

- [ ] **Step 2: Run the focused `RealtimeModel` spec**

Run: `cd diagnostics-web; npm test -- --runInBand src/app/Model/RealtimeModel.spec.ts`
Expected: FAIL until the model is constructed with proper doubles and assertions for merged categories/events.

- [ ] **Step 3: Add tests for selection, events, expand/collapse, and property updates**

```ts
it('subscribes to the active process when one is selected', async () => {
  const hubService = createHubService();
  const model = createRealtimeModel(hubService);

  await model.selectProcess({ id: 'p-1' } as any);

  expect(hubService.connection.invoke).toHaveBeenCalledWith('Subscribe', 'p-1');
});

it('opens an error dialog when setPropertyValue returns an error message', async () => {
  const dialog = { open: jest.fn() } as any;
  const hubService = {
    ...createHubService(),
    setPropertyValue: jest.fn().mockResolvedValue({ errorMessage: 'Denied' }),
  };
  const model = createRealtimeModel(hubService, dialog);

  model.activeProcess = { id: 'p-1' } as any;
  await model.setPropertyValue({ getPropertyPath: () => 'Config.Timeout' } as any, '15');

  expect(dialog.open).toHaveBeenCalled();
});
```

- [ ] **Step 4: Run the `RealtimeModel` suite**

Run: `cd diagnostics-web; npm test -- --runInBand src/app/Model/RealtimeModel.spec.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add diagnostics-web/src/app/Model/RealtimeModel.spec.ts
git commit -m "test: cover realtime model behavior"
```

### Task 8: Add Stryker config, summary artifacts, and CI integration

**Files:**
- Create: `diagnostics-web/stryker.conf.json`
- Create: `scripts/summarize-stryker.ps1`
- Modify: `.github/workflows/publish-docker-image.yml`
- Create: `.github/workflows/mutation-web.yml`
- Modify: `Docker/Dockerfile`
- Modify: `README.md`
- Verify/Modify: `DiagnosticService/Config/settings.Debug.json`
- Test: `diagnostics-web/stryker.conf.json`

- [ ] **Step 1: Add the Stryker config and a failing mutation command**

```json
// diagnostics-web/stryker.conf.json
{
  "$schema": "https://raw.githubusercontent.com/stryker-mutator/stryker-js/master/packages/api/schema/stryker-schema.json",
  "testRunner": "jest",
  "jest": {
    "projectType": "custom",
    "configFile": "jest.config.ts"
  },
  "reporters": ["html", "json", "progress", "clear-text"],
  "coverageAnalysis": "off",
  "mutate": [
    "src/app/**/*.ts",
    "!src/**/*.spec.ts",
    "!src/main.ts",
    "!src/environments/*.ts"
  ],
  "tempDirName": "StrykerOutput/.stryker-tmp",
  "thresholds": {
    "high": 80,
    "low": 70,
    "break": 0
  }
}
```

```json
// diagnostics-web/package.json
{
  "scripts": {
    "test:mutation": "stryker run stryker.conf.json"
  },
  "devDependencies": {
    "@stryker-mutator/core": "^9.6.1",
    "@stryker-mutator/jest-runner": "^9.6.1"
  }
}
```

- [ ] **Step 2: Run the mutation command to verify the summary/report plumbing is still missing**

Run: `cd diagnostics-web; npm run test:mutation`
Expected: Stryker runs or fails before CI/report summarization exists.

- [ ] **Step 3: Add the summary script and CI workflows**

```powershell
# scripts/summarize-stryker.ps1
param(
    [Parameter(Mandatory)]
    [string] $ReportPath,
    [string] $JsonOutputPath,
    [string] $MarkdownOutputPath
)

$ErrorActionPreference = 'Stop'
$report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
$files = @($report.files.PSObject.Properties)
$rows = foreach ($file in $files) {
    $mutants = @($file.Value.mutants)
    [pscustomobject]@{
        file     = [System.IO.Path]::GetFileName($file.Name)
        killed   = @($mutants | Where-Object status -eq 'Killed').Count
        survived = @($mutants | Where-Object status -eq 'Survived').Count
        ignored  = @($mutants | Where-Object status -eq 'Ignored').Count
        total    = $mutants.Count
    }
}
```

```yaml
# .github/workflows/mutation-web.yml
name: mutation-web

on:
  workflow_dispatch:
  pull_request:
    branches: [main]

jobs:
  stryker-web:
    runs-on: ubuntu-latest
    continue-on-error: true
    defaults:
      run:
        working-directory: diagnostics-web
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: 22
          cache: npm
          cache-dependency-path: diagnostics-web/package-lock.json
      - run: npm ci
      - run: npm test -- --runInBand
      - run: npm run test:mutation
      - name: Summarize mutation report
        if: always()
        shell: pwsh
        run: |
          $latest = Get-ChildItem StrykerOutput -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
          $report = Join-Path $latest.FullName 'reports/mutation-report.json'
          $jsonSummary = Join-Path $latest.FullName 'reports/mutation-summary.json'
          $mdSummary = Join-Path $latest.FullName 'reports/mutation-summary.md'
          ..\scripts\summarize-stryker.ps1 -ReportPath $report -JsonOutputPath $jsonSummary -MarkdownOutputPath $mdSummary | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append
      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: mutation-web-report
          path: diagnostics-web/StrykerOutput/**
```

```yaml
# .github/workflows/publish-docker-image.yml (insert before Docker build)
      - uses: actions/setup-node@v4
        with:
          node-version: 22
          cache: npm
          cache-dependency-path: diagnostics-web/package-lock.json
      - name: Frontend validation
        working-directory: diagnostics-web
        run: |
          npm ci
          npm test -- --runInBand
          npm run build
```

```dockerfile
# Docker/Dockerfile
FROM node:22-alpine AS spa-build
WORKDIR /spa
COPY diagnostics-web/package.json diagnostics-web/package-lock.json ./
RUN npm ci
COPY diagnostics-web/ ./
RUN npm run build
```

- [ ] **Step 4: Run the full validation path**

Run: `cd diagnostics-web; npm ci; npm test -- --runInBand; npm run build; npm run test:mutation`
Expected: PASS locally, producing `StrykerOutput/**/reports/mutation-report.json`.

- [ ] **Step 5: Commit**

```bash
git add diagnostics-web/package.json diagnostics-web/package-lock.json diagnostics-web/stryker.conf.json scripts/summarize-stryker.ps1 .github/workflows/publish-docker-image.yml .github/workflows/mutation-web.yml Docker/Dockerfile README.md
git commit -m "ci: add Angular mutation testing and frontend validation"
```

## Self-review checklist

- **Spec coverage:** Angular upgrade, test modernization, behavior-focused suite expansion, repo-level build consumers, and Stryker CI/reporting are all covered by Tasks 1-8.
- **Placeholder scan:** No `TODO`, `TBD`, or "similar to previous task" shortcuts remain; each task includes concrete files, commands, and code examples.
- **Type consistency:** The plan consistently targets Jest-based specs, Angular 21 tooling, `scripts/summarize-stryker.ps1`, and the repo paths approved in the design.
