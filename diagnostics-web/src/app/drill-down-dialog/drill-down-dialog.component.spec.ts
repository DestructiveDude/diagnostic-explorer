import {TestBed} from '@angular/core/testing';
import {By} from '@angular/platform-browser';
import {DatePipe} from '@angular/common';
import {firstValueFrom, Subject} from 'rxjs';
import {DynamicDialogConfig, DialogService} from 'primeng/dynamicdialog';
import {AppModule} from '../app.module';
import {DrillDownDialogComponent} from './drill-down-dialog.component';
import {RealtimeCategoryComponent} from '../realtime-category/realtime-category.component';
import {EventSinkViewComponent} from '../event-sink-view/event-sink-view.component';
import {DrillDownRequest, DrillDownResponse} from '../Model/DrillDownRequest';
import {Category, DiagnosticResponse, Operation, OperationSet, Property, PropertyBag} from '../Model/DiagResponse';
import {RealtimeModel} from '../Model/RealtimeModel';
import {CategoryModel} from '../Model/CategoryModel';
import {ExecOperationsModel} from '../Model/ExecOperationsModel';
import {LogStreamEvent} from '../Model/LogStream';
import {FilterCriteria} from '../Model/FilterCriteria';

const fencedName = 'Orders[2]\u001fabcd1234';
const outerPath = 'Trading|Engine||Orders';

function diagnostics(): DiagnosticResponse {
    return Object.assign(new DiagnosticResponse(), {
        propertyBags: [Object.assign(new PropertyBag(), {
            name: fencedName, category: 'Trading', canDrillDown: false, operationSet: 'nested-ops',
            categories: [Object.assign(new Category(), {
                name: 'State', canDrillDown: true,
                properties: [Object.assign(new Property(), {
                    name: 'Order', value: 'Order value', canSet: true, canDrillDown: true,
                    canJsonHover: true, canExpandedHover: true, drillDownText: 'Open order'
                })]
            })]
        })],
        operationSets: [Object.assign(new OperationSet(), {
            id: 'nested-ops', operations: [Object.assign(new Operation(), {signature: 'Reset()'})]
        })]
    });
}

describe('drilldown UI', () => {
    const originalResizeObserver = globalThis.ResizeObserver;
    let realtime: RealtimeModel;
    let hub: {getDrillDown: jest.Mock, setPropertyValue: jest.Mock, executeOperation: jest.Mock};
    let config: DynamicDialogConfig;
    let dialogs: {open: jest.Mock};

    beforeAll(() => {
        // jsdom has no layout observer; PrimeNG tabs still register one when opened.
        globalThis.ResizeObserver = jest.fn().mockImplementation(() => ({
            observe: jest.fn(), unobserve: jest.fn(), disconnect: jest.fn()
        }));
    });

    afterAll(() => {
        globalThis.ResizeObserver = originalResizeObserver;
    });

    beforeEach(async () => {
        hub = {
            getDrillDown: jest.fn().mockResolvedValue(Object.assign(new DrillDownResponse(), {
                diagnostics: diagnostics(), displayedCount: 1, totalCount: 4, isTruncated: true
            })),
            setPropertyValue: jest.fn().mockResolvedValue({isSuccess: true}),
            executeOperation: jest.fn().mockResolvedValue({isSuccess: true, result: 'Reset'})
        };
        dialogs = {open: jest.fn()};
        realtime = new RealtimeModel({
            ...hub, connectionReady: new Subject(), connectionStarted: new Subject()
        } as any, new DatePipe('en-US'), dialogs as any, {add: jest.fn()} as any);
        realtime.activeProcess = {id: 'original'} as any;
        config = {data: {title: 'Orders', realtime,
            request: {...new DrillDownRequest(), id: 'original', objectPaths: [outerPath]}}};
        await TestBed.configureTestingModule({
            imports: [AppModule],
            providers: [
                {provide: DynamicDialogConfig, useValue: config},
                {provide: RealtimeModel, useValue: realtime},
                {provide: DialogService, useValue: dialogs}
            ]
        }).compileComponents();
    });

    async function render() {
        const fixture = TestBed.createComponent(DrillDownDialogComponent);
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
        return fixture;
    }

    it('renders affordances and truncation, hides the fence, and navigates with the complete path', async () => {
        const fixture = await render();
        const element: HTMLElement = fixture.nativeElement;
        expect(element.textContent).toContain('Showing 1 of 4 item(s) (truncated)');
        expect(element.textContent).toContain('Orders[2]');
        expect(element.textContent).not.toContain('abcd1234');
        expect(element.querySelector('button[aria-label="Inspect Orders[2]"]')).not.toBeNull();
        expect(element.querySelector('button[aria-label="Inspect State"]')).not.toBeNull();
        expect(element.querySelector('button[aria-label="Inspect Order"]')?.textContent).toContain('Open order');
        (element.querySelector('button[aria-label="Inspect Order"]') as HTMLButtonElement).click();
        expect(hub.getDrillDown).toHaveBeenLastCalledWith(expect.objectContaining({
            id: 'original', objectPaths: [outerPath, `Trading|${fencedName}|State|Order`]
        }));
        fixture.componentInstance.goBack(0);
        expect(hub.getDrillDown).toHaveBeenLastCalledWith(config.data.request);
    });

    it('updates opt-in flags and retains the value for icon-only drilldown', async () => {
        const fixture = await render();
        const grid = fixture.debugElement.query(By.directive(RealtimeCategoryComponent)).componentInstance as RealtimeCategoryComponent;
        const source = diagnostics().propertyBags[0];
        source.name = 'Single object';
        source.canDrillDown = false;
        source.categories[0].canDrillDown = false;
        Object.assign(source.categories[0].properties[0], {drillDownIconOnly: true, canExpandedHover: false, canJsonHover: false});
        grid.category!.update([source]);
        fixture.detectChanges();
        const element: HTMLElement = fixture.nativeElement;
        expect(element.textContent).toContain('Order value');
        expect(element.querySelector('button[aria-label="Open order"]')).not.toBeNull();
        expect(element.querySelector('button[aria-label="Inspect Orders[2]"]')).toBeNull();
        expect(element.querySelector('button[aria-label="Inspect Single object"]')).toBeNull();
        expect(element.querySelector('button[aria-label="Inspect State"]')).toBeNull();
        expect(element.querySelector('button[aria-label="JSON for Order"]')).toBeNull();
    });

    it('opens an already-gated collection item even though drilldown bags report canDrillDown=false', async () => {
        const fixture = await render();
        (fixture.nativeElement.querySelector('button[aria-label="Inspect Orders[2]"]') as HTMLButtonElement).click();
        expect(hub.getDrillDown).toHaveBeenLastCalledWith(expect.objectContaining({
            objectPaths: [outerPath, `Trading|${fencedName}`]
        }));
    });

    it.each([true, false])('loads a keyboard-accessible preview, json=%s, without subscribing to events', async json => {
        const fixture = await render();
        hub.getDrillDown.mockResolvedValue({...new DrillDownResponse(), errorMessage: 'Preview limit exceeded'});
        const label = json ? 'JSON for Order' : 'Preview Order';
        const button = fixture.nativeElement.querySelector(`button[aria-label="${label}"]`) as HTMLButtonElement;
        button.dispatchEvent(new FocusEvent('focus'));
        await fixture.whenStable();
        fixture.detectChanges();
        expect(hub.getDrillDown).toHaveBeenLastCalledWith(expect.objectContaining({jsonHover: json, excludeEventViews: true}));
        expect(document.querySelector('.property-preview-tooltip')?.textContent).toContain('Preview limit exceeded');
        button.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape', bubbles: true}));
        fixture.detectChanges();
        expect(document.querySelector('.property-preview-tooltip')).toBeNull();
    });

    it.each(['agent', 'transport'])('renders %s errors and clears stale diagnostics', async kind => {
        const fixture = await render();
        if (kind === 'agent') hub.getDrillDown.mockResolvedValue({...new DrillDownResponse(), errorMessage: 'Depth limit exceeded'});
        else hub.getDrillDown.mockRejectedValue(new Error('Connection lost'));
        await fixture.componentInstance.refresh();
        fixture.detectChanges();
        expect(fixture.nativeElement.querySelector('[role="alert"]').textContent)
            .toContain(kind === 'agent' ? 'Depth limit exceeded' : 'Connection lost');
        expect(fixture.nativeElement.querySelector('app-realtime-category')).toBeNull();
    });

    it('ignores an older response and a response after the dialog closes', async () => {
        const component = new DrillDownDialogComponent(config, realtime);
        let resolve!: (value: DrillDownResponse) => void;
        hub.getDrillDown.mockReturnValueOnce(new Promise<DrillDownResponse>(done => resolve = done));
        const pending = component.refresh();
        await component.refresh();
        const latest = component.response;
        resolve({...new DrillDownResponse(), errorMessage: 'Stale'});
        await pending;
        expect(component.response).toBe(latest);
        hub.getDrillDown.mockReturnValueOnce(new Promise<DrillDownResponse>(done => resolve = done));
        const closing = component.refresh();
        component.ngOnDestroy();
        resolve(new DrillDownResponse());
        await closing;
        expect(component.response).toBeUndefined();
    });

    it('keeps setters and operations on the originating process, fenced path, and operation set', async () => {
        const fixture = await render();
        const grid = fixture.debugElement.query(By.directive(RealtimeCategoryComponent)).componentInstance as RealtimeCategoryComponent;
        const closed = new Subject();
        dialogs.open.mockReturnValue({onClose: closed, close: jest.fn()});
        const refresh = jest.spyOn(fixture.componentInstance, 'refresh');
        grid.showSetPropertyDialog(grid.category!.subCats[0].groups[0].properties[0]);
        realtime.activeProcess = {id: 'other'} as any;
        const completed = firstValueFrom(grid.actionCompleted);
        closed.next({button: 'OK', value: '15'});
        await completed;
        expect(hub.setPropertyValue).toHaveBeenCalledWith(expect.objectContaining({
            id: 'original', objectPaths: [outerPath], path: `Trading|${fencedName}|State|Order`, value: '15'
        }));
        expect(refresh).toHaveBeenCalled();

        const category = new CategoryModel(realtime, 'Trading', diagnostics().propertyBags);
        const model = new ExecOperationsModel(realtime, category.subCats[0], {copy: jest.fn()} as any,
            config.data.request, diagnostics().operationSets);
        model.selectOperation(model.operations[0]);
        await model.execute();
        expect(hub.executeOperation).toHaveBeenCalledWith(expect.objectContaining({
            id: 'original', objectPaths: [outerPath], path: `Trading|${fencedName}`, operation: 'Reset()'
        }));
        expect(model.results).toBe('Reset');
    });

    it('projects matchers once per event, filters on display levels, and renders safe event detail', () => {
        const fixture = TestBed.createComponent(EventSinkViewComponent);
        const component = fixture.componentInstance;
        const event: LogStreamEvent = {streamId: 's', sequence: 1, timestampUtc: '2026-09-07T12:00:00Z',
            loggerCategory: 'App.Order.Child', level: 3, message: 'Warning', detail: '<script>bad()</script>', eventId: 42};
        fixture.componentRef.setInput('view', {name: 'Orders', category: 'Trading', id: 'orders', matchers: [
            {loggerName: 'App.Order', loggerNameMatchMode: 'Prefix', minLevel: 3},
            {loggerName: 'app.order.child', loggerNameMatchMode: 'Exact'}
        ]});
        fixture.componentRef.setInput('events', [event, {...event, sequence: 2, loggerCategory: 'App.Orders'},
            {...event, sequence: 3, loggerCategory: 'App.Order.Other', level: 2}]);
        fixture.detectChanges();
        expect(component.rows).toHaveLength(1);
        (fixture.nativeElement.querySelector('tbody button') as HTMLButtonElement).click();
        fixture.detectChanges();
        expect(fixture.nativeElement.textContent).toContain('Event 42');
        expect(fixture.nativeElement.textContent).toContain('<script>bad()</script>');
        expect(fixture.nativeElement.querySelector('script')).toBeNull();
        const criteria = Object.assign(new FilterCriteria(), {error: true});
        component.filter(criteria);
        expect(component.filteredRows).toHaveLength(0);
        expect(component.selected).toBeUndefined();
        component.filter(new FilterCriteria());
        component.selected = event;
        fixture.componentRef.setInput('events', [{...event, streamId: 'new-stream'}]);
        fixture.detectChanges();
        expect(component.selected).toBeUndefined();
    });

    it('retains its process event stream and displays that store after main selection changes', async () => {
        const event = {streamId: 'original-stream', sequence: 1, timestampUtc: '2026-09-08T10:00:00Z',
            loggerCategory: 'App.Order', level: 2, message: 'Original'} as LogStreamEvent;
        realtime.getProcessEventStore('original').initialize({streamId: 'original-stream', routing: {matchMode: 'AllMatches', routes: []},
            replayEvents: [event], highWatermark: 1, maxEvents: 10, maxAgeMinutes: 10});
        hub.getDrillDown.mockResolvedValue(Object.assign(new DrillDownResponse(), {
            diagnostics: diagnostics(), eventViews: [{id: 'events', category: 'Trading', name: 'Orders', matchers: [
                {loggerName: '*', loggerNameMatchMode: 'Wildcard', minLevel: null, maxLevel: null}
            ]}]
        }));
        const release = jest.fn();
        jest.spyOn(realtime, 'retainProcessEvents').mockReturnValue(release);

        const fixture = await render();
        realtime.activeProcess = {id: 'other'} as any;
        fixture.detectChanges();

        const eventView = fixture.debugElement.query(By.directive(EventSinkViewComponent)).componentInstance as EventSinkViewComponent;
        expect(realtime.retainProcessEvents).toHaveBeenCalledWith('original');
        expect(eventView.events).toEqual([event]);
        fixture.destroy();
        expect(release).toHaveBeenCalledTimes(1);
    });

    it('refreshes after closing an operation dialog that executed more than once', async () => {
        const fixture = await render();
        const grid = fixture.debugElement.query(By.directive(RealtimeCategoryComponent)).componentInstance as RealtimeCategoryComponent;
        const closed = new Subject();
        dialogs.open.mockReturnValue({onClose: closed, close: jest.fn()});
        const refresh = jest.spyOn(fixture.componentInstance, 'refresh');
        grid.showOperationsDialog(new MouseEvent('click'), grid.category!.subCats[0]);
        const model = dialogs.open.mock.calls.at(-1)![1].data as ExecOperationsModel;
        model.selectOperation(model.operations[0]);
        await model.execute();
        await model.execute();
        expect(refresh).not.toHaveBeenCalled();
        closed.next(undefined);
        expect(refresh).toHaveBeenCalledTimes(1);
    });

    it('does not refresh when the operations dialog closes without executing', async () => {
        const fixture = await render();
        const grid = fixture.debugElement.query(By.directive(RealtimeCategoryComponent)).componentInstance as RealtimeCategoryComponent;
        const closed = new Subject();
        dialogs.open.mockReturnValue({onClose: closed, close: jest.fn()});
        const refresh = jest.spyOn(fixture.componentInstance, 'refresh');
        grid.showOperationsDialog(new MouseEvent('click'), grid.category!.subCats[0]);
        closed.next(undefined);
        expect(refresh).not.toHaveBeenCalled();
    });

    it.each([true, false])('refreshes after an in-flight operation settles even if closed early, success=%s', async isSuccess => {
        const fixture = await render();
        const grid = fixture.debugElement.query(By.directive(RealtimeCategoryComponent)).componentInstance as RealtimeCategoryComponent;
        const closed = new Subject();
        dialogs.open.mockReturnValue({onClose: closed, close: jest.fn()});
        const refresh = jest.spyOn(fixture.componentInstance, 'refresh');
        let resolve!: (value: unknown) => void;
        hub.executeOperation.mockReturnValue(new Promise(done => resolve = done));
        grid.showOperationsDialog(new MouseEvent('click'), grid.category!.subCats[0]);
        const model = dialogs.open.mock.calls.at(-1)![1].data as ExecOperationsModel;
        model.selectOperation(model.operations[0]);
        const executing = model.execute();
        closed.next(undefined);
        expect(refresh).not.toHaveBeenCalled();
        resolve({isSuccess, errorMessage: isSuccess ? '' : 'Partially failed'});
        await executing;
        expect(refresh).toHaveBeenCalledTimes(1);
    });
});
