import {CUSTOM_ELEMENTS_SCHEMA} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {AppComponent} from './app.component';
import {AppModel} from './Model/AppModel';

/**
 * Characterization coverage for the app shell. The component renders the
 * PrimeNG splitter shell and a tree of feature components, and its constructor
 * kicks off AppModel.start() (which opens a SignalR connection in production).
 * We stub AppModel so construction is side-effect free and allow the child
 * elements through CUSTOM_ELEMENTS_SCHEMA, then assert the real shell renders
 * rather than the long-removed Angular CLI placeholder.
 */
describe('AppComponent', () => {
    let startCalls: number;
    let appModelStub: Partial<AppModel>;

    beforeEach(async () => {
        startCalls = 0;
        appModelStub = {
            tabIndex: 0,
            titleMessage: '',
            mainMessage: 'Connecting',
            mainMessageClass: '',
            mainMessageCanClick: false,
            mainMessageClick: () => undefined,
            viewRealtime: () => undefined,
            viewRetro: () => undefined,
            start: () => {
                startCalls++;
                return Promise.resolve();
            },
        };

        await TestBed.configureTestingModule({
            declarations: [AppComponent],
            schemas: [CUSTOM_ELEMENTS_SCHEMA],
        })
            .overrideComponent(AppComponent, {
                set: {providers: [{provide: AppModel, useValue: appModelStub}]},
            })
            .compileComponents();
    });

    it('creates the app shell and starts the diagnostics connection on load', () => {
        const fixture = TestBed.createComponent(AppComponent);

        expect(fixture.componentInstance).toBeTruthy();
        expect(startCalls).toBe(1);
    });

    it('renders the splitter shell rather than the Angular CLI placeholder', () => {
        const fixture = TestBed.createComponent(AppComponent);
        fixture.detectChanges();

        const compiled: HTMLElement = fixture.nativeElement;
        expect(compiled.querySelector('p-splitter')).not.toBeNull();
        expect(compiled.textContent).toContain('Diagnostic Explorer');
        expect(compiled.textContent).not.toContain('app is running!');
    });
});
