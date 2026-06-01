import {DatePipe} from '@angular/common';
import {RealtimeModel} from './RealtimeModel';
import {DiagnosticResponse, OperationSet, PropertyBag, SystemEvent} from './DiagResponse';
import {DiagProcess} from './DiagProcess';
import {Level} from './Level';

/**
 * A fake hub connection that records the handlers RealtimeModel registers via
 * `connection.on(name, handler)`, so a test can fire an inbound message by
 * invoking the captured handler directly.
 */
function makeConnection() {
    const handlers: Record<string, (...args: any[]) => void> = {};
    return {
        on: jest.fn((name: string, handler: (...args: any[]) => void) => {
            handlers[name] = handler;
        }),
        invoke: jest.fn().mockResolvedValue(undefined),
        handlers,
    };
}

/**
 * A fake DiagHubService. connectionReady / connectionStarted capture their
 * subscriber so the test can emit a connection on demand and exercise the
 * wiring set up in the model's constructor.
 */
function makeHub() {
    let readyCb: ((c: any) => void) | undefined;
    let startedCb: ((c: any) => void) | undefined;
    return {
        connectionReady: {subscribe: jest.fn((cb: (c: any) => void) => (readyCb = cb))},
        connectionStarted: {subscribe: jest.fn((cb: (c: any) => void) => (startedCb = cb))},
        connection: {invoke: jest.fn().mockResolvedValue(undefined)},
        setPropertyValue: jest.fn().mockResolvedValue({}),
        removeProcess: jest.fn().mockResolvedValue(undefined),
        emitReady(c: any) { readyCb?.(c); },
        emitStarted(c: any) { startedCb?.(c); },
    };
}

function makeModel(hub = makeHub(), dialog = {open: jest.fn()}, snackBar = {open: jest.fn()}) {
    const model = new RealtimeModel(hub as any, new DatePipe('en-US'), dialog as any, snackBar as any);
    return {model, hub, dialog, snackBar};
}

function proc(id: string, name: string, state = 'Online', machine = 'SRV', user = 'svc') {
    return {id, processName: name, machineName: machine, userName: user, state} as any;
}

function evt(over: Partial<SystemEvent>): SystemEvent {
    return Object.assign(new SystemEvent(), over);
}

describe('RealtimeModel', () => {
    describe('process list filtering', () => {
        it('filters processes by online state and search text', () => {
            const {model} = makeModel();

            model.displayProcesses([
                proc('1', 'OrderWorker', 'Online'),
                proc('2', 'AuditWorker', 'Offline'),
            ]);

            // onlineOnly defaults true, so the offline process is already excluded.
            expect(model.filteredProcesses.map(p => p.id)).toEqual(['1']);

            model.processSearch = 'order';

            expect(model.filteredProcesses.map(p => p.id)).toEqual(['1']);
        });

        it('shows every process, online or not, once onlineOnly is cleared', () => {
            const {model} = makeModel();
            model.displayProcesses([
                proc('1', 'OrderWorker', 'Online'),
                proc('2', 'AuditWorker', 'Offline'),
            ]);

            model.onlineOnly = false;

            expect(model.filteredProcesses.map(p => p.id).sort()).toEqual(['1', '2']);
        });

        it('falls back to an escaped regex when the search text is not a valid pattern', () => {
            const {model} = makeModel();
            model.displayProcesses([
                proc('1', 'Worker(1)', 'Online'),
                proc('2', 'Worker(2)', 'Online'),
            ]);

            // 'Worker(1' has an unbalanced paren — an invalid regex; createFilterRegex
            // must escape it and match literally rather than throw.
            model.processSearch = 'Worker(1';

            expect(model.filteredProcesses.map(p => p.id)).toEqual(['1']);
        });

        it('removes a process from both the full and filtered lists', () => {
            const {model} = makeModel();
            model.displayProcesses([
                proc('1', 'OrderWorker'),
                proc('2', 'PayWorker'),
            ]);

            model.removeProcess('1');

            expect(model.allProcesses.map(p => p.id)).toEqual(['2']);
            expect(model.filteredProcesses.map(p => p.id)).toEqual(['2']);
        });

        it('resets the search when Escape is pressed and ignores other keys', () => {
            const {model} = makeModel();
            model.processSearch = 'order';

            model.handleKeyDown({key: 'a'} as KeyboardEvent);
            expect(model.processSearch).toBe('order');

            model.handleKeyDown({key: 'Escape'} as KeyboardEvent);
            expect(model.processSearch).toBeNull();
        });
    });

    describe('SignalR wiring', () => {
        it('registers a handler for each inbound message on connectionReady', () => {
            const {hub} = makeModel();
            const connection = makeConnection();

            hub.emitReady(connection);

            expect(Object.keys(connection.handlers).sort()).toEqual([
                'RemoveProcess', 'SetEvents', 'SetProcesses', 'ShowDiagnostics',
                'ShowDiagnosticsError', 'StreamEvents', 'UpdateProcess',
            ]);
        });

        it('routes SetProcesses to a full process refresh', () => {
            const {model, hub} = makeModel();
            const connection = makeConnection();
            hub.emitReady(connection);

            connection.handlers['SetProcesses']([proc('1', 'OrderWorker'), proc('2', 'PayWorker')]);

            expect(model.allProcesses.map(p => p.id).sort()).toEqual(['1', '2']);
        });

        it('routes UpdateProcess to a merge that keeps existing processes', () => {
            const {model, hub} = makeModel();
            const connection = makeConnection();
            hub.emitReady(connection);
            connection.handlers['SetProcesses']([proc('1', 'OrderWorker', 'Online'), proc('2', 'PayWorker', 'Online')]);

            connection.handlers['UpdateProcess'](proc('1', 'OrderWorker', 'Offline'));

            // Both processes remain; the update is applied in place, not replaced wholesale.
            expect(model.allProcesses.map(p => p.id).sort()).toEqual(['1', '2']);
            expect(model.allProcesses.find(p => p.id === '1')!.state).toBe('Offline');
        });

        it('routes RemoveProcess to a removal', () => {
            const {model, hub} = makeModel();
            const connection = makeConnection();
            hub.emitReady(connection);
            connection.handlers['SetProcesses']([proc('1', 'OrderWorker'), proc('2', 'PayWorker')]);

            connection.handlers['RemoveProcess']('1');

            expect(model.allProcesses.map(p => p.id)).toEqual(['2']);
        });

        it('routes ShowDiagnosticsError to a snackbar', () => {
            const {model, hub, snackBar} = makeModel();
            const connection = makeConnection();
            hub.emitReady(connection);
            model.activeProcess = proc('p-1', 'Active');

            connection.handlers['ShowDiagnosticsError']('p-1', 'boom');

            expect(snackBar.open).toHaveBeenCalledWith('boom', '', {duration: 2_000});
        });

        it('subscribes the active process when the connection (re)starts', () => {
            const {model, hub} = makeModel();
            model.activeProcess = proc('p-7', 'Worker');

            hub.emitStarted(hub.connection);

            expect(hub.connection.invoke).toHaveBeenCalledWith('Subscribe', 'p-7');
        });
    });

    describe('displayRealtimeDiags', () => {
        it('groups property bags into sorted categories and stores the operation sets', () => {
            const {model, hub} = makeModel();
            const connection = makeConnection();
            hub.emitReady(connection);
            model.activeProcess = proc('p-1', 'Active');

            const response = new DiagnosticResponse();
            response.propertyBags = [
                Object.assign(new PropertyBag(), {name: 'b', category: 'Zeta'}),
                Object.assign(new PropertyBag(), {name: 'a', category: 'Alpha'}),
            ];
            response.operationSets = [Object.assign(new OperationSet(), {id: 'ops-1'})];

            connection.handlers['ShowDiagnostics']('p-1', response);

            expect(model.categories.map(c => c.name)).toEqual(['Alpha', 'Zeta']);
            expect(model.operationSets.map(o => o.id)).toEqual(['ops-1']);
            expect(model.titleMessage).toMatch(/^Received at /);
        });

        it('updates an existing category in place on a subsequent diagnostics push', () => {
            const {model, hub} = makeModel();
            const connection = makeConnection();
            hub.emitReady(connection);
            model.activeProcess = proc('p-1', 'Active');

            const first = new DiagnosticResponse();
            first.propertyBags = [Object.assign(new PropertyBag(), {name: 'a', category: 'Alpha'})];
            connection.handlers['ShowDiagnostics']('p-1', first);
            const original = model.categories.find(c => c.name === 'Alpha')!;

            const second = new DiagnosticResponse();
            second.propertyBags = [Object.assign(new PropertyBag(), {name: 'a2', category: 'Alpha'})];
            connection.handlers['ShowDiagnostics']('p-1', second);

            // Same CategoryModel instance is retained and its property data refreshed,
            // rather than the category being recreated.
            const updated = model.categories.find(c => c.name === 'Alpha')!;
            expect(updated).toBe(original);
            expect(updated.propData.map(p => p.name)).toEqual(['a2']);
        });

        it('ignores a ShowDiagnostics frame for a non-active process (id guard)', () => {
            const {model, hub} = makeModel();
            const connection = makeConnection();
            hub.emitReady(connection);
            model.activeProcess = proc('p-1', 'Active');

            const response = new DiagnosticResponse();
            response.propertyBags = [Object.assign(new PropertyBag(), {name: 'a', category: 'Alpha'})];

            // A late frame for a different (previously-selected) process must not overwrite the view.
            connection.handlers['ShowDiagnostics']('p-OTHER', response);

            expect(model.categories).toEqual([]);
        });
    });

    describe('event streaming', () => {
        it('ignores streamed events for a process that is not the active one', () => {
            const {model, hub} = makeModel();
            const connection = makeConnection();
            hub.emitReady(connection);
            model.activeProcess = proc('active', 'Worker');

            connection.handlers['StreamEvents']('other', [evt({sinkCategory: 'Cat', sinkName: 'Sink', severity: 'High'})]);

            expect(model.categories).toHaveLength(0);
        });

        it('creates a category per sinkCategory and derives the level from severity', () => {
            const {model, hub} = makeModel();
            const connection = makeConnection();
            hub.emitReady(connection);
            model.activeProcess = proc('active', 'Worker');

            connection.handlers['StreamEvents']('active', [
                evt({sinkCategory: 'Disk', sinkName: 'IO', severity: 'Medium'}),
                evt({sinkCategory: 'Net', sinkName: 'Http', severity: 'Low'}),
            ]);

            expect(model.categories.map(c => c.name).sort()).toEqual(['Disk', 'Net']);
            // severity Medium -> WARN, Low -> INFO; worstSev is the max event level in the category.
            expect(model.categories.find(c => c.name === 'Disk')!.worstSev).toBe(Level.WARN);
            expect(model.categories.find(c => c.name === 'Net')!.worstSev).toBe(Level.INFO);
        });

        it('keeps an explicit level instead of deriving it from severity', () => {
            const {model, hub} = makeModel();
            const connection = makeConnection();
            hub.emitReady(connection);
            model.activeProcess = proc('active', 'Worker');

            connection.handlers['StreamEvents']('active', [
                evt({sinkCategory: 'Disk', sinkName: 'IO', severity: 'Low', level: Level.ERROR}),
            ]);

            expect(model.categories.find(c => c.name === 'Disk')!.worstSev).toBe(Level.ERROR);
        });

        it('clears existing event sinks for the active process before applying SetEvents', () => {
            const {model, hub} = makeModel();
            const connection = makeConnection();
            hub.emitReady(connection);
            model.activeProcess = proc('active', 'Worker');

            connection.handlers['SetEvents']('active', [evt({sinkCategory: 'Disk', sinkName: 'IO', severity: 'High'})]);
            const firstSink = model.categories.find(c => c.name === 'Disk')!.eventSinks[0];

            connection.handlers['SetEvents']('active', [evt({sinkCategory: 'Disk', sinkName: 'IO', severity: 'High'})]);
            const secondSink = model.categories.find(c => c.name === 'Disk')!.eventSinks[0];

            // SetEvents resets eventSinks, so the sink is rebuilt rather than accumulated.
            expect(secondSink).not.toBe(firstSink);
        });
    });

    describe('selection and display state', () => {
        it('clears active-process state when the active process is removed', () => {
            const {model} = makeModel();
            const active = proc('p-1', 'Worker');
            const selected = {isSelected: true} as any;

            model.activeProcess = active;
            model.filteredProcesses = [active];
            model.allProcesses = [active];
            model.categories = [{name: 'A'} as any];
            model.activeCat = model.categories[0];
            model.selectedEvent = selected;
            model.traceScopeVisible = true;

            model.removeProcess('p-1');

            expect(model.activeProcess).toBeNull();
            expect(model.categories).toEqual([]);
            expect(model.activeCat).toBeUndefined();
            expect(model.selectedEvent).toBeUndefined();
            expect(selected.isSelected).toBe(false);
            expect(model.traceScopeVisible).toBe(false);
        });

        it('selects an event and opens the trace scope', () => {
            const {model} = makeModel();
            const previous = {isSelected: true} as any;
            const next = {isSelected: false} as any;
            model.selectedEvent = previous;

            model.setCurrentEvent(next);

            expect(previous.isSelected).toBe(false);
            expect(next.isSelected).toBe(true);
            expect(model.selectedEvent).toBe(next);
            expect(model.traceScopeVisible).toBe(true);
        });

        it('selects the event under the pointer only while the primary button is held', () => {
            const {model} = makeModel();
            const item = {isSelected: false} as any;

            model.handleMouseOver(item, {buttons: 0} as MouseEvent);
            expect(model.selectedEvent).toBeUndefined();

            model.handleMouseOver(item, {buttons: 1} as MouseEvent);
            expect(model.selectedEvent).toBe(item);
        });

        it('hides the trace scope', () => {
            const {model} = makeModel();
            model.traceScopeVisible = true;

            model.hideTraceScope();

            expect(model.traceScopeVisible).toBe(false);
        });

        it('switches the active tab between realtime and retro', () => {
            const {model} = makeModel();

            model.viewRetro();
            expect(model.tabIndex).toBe(1);

            model.viewRealtime();
            expect(model.tabIndex).toBe(0);
        });

        it('tracks the active category by selected tab index', () => {
            const {model} = makeModel();
            model.categories = [{name: 'A'} as any, {name: 'B'} as any];

            model.handleSelectedTabChanged(1);

            expect(model.activeCat?.name).toBe('B');
        });

        it('expands all of the active category when some are collapsed, and collapses when all are expanded', () => {
            const {model} = makeModel();
            model.activeCat = {
                subCats: [{isExpanded: true}, {isExpanded: false}],
                eventSinks: [{isExpanded: true}],
            } as any;

            model.expandCollapse();
            // mixed -> not all expanded -> expand everything
            expect(model.activeCat!.subCats.every(s => s.isExpanded)).toBe(true);
            expect(model.activeCat!.eventSinks.every(s => s.isExpanded)).toBe(true);

            model.expandCollapse();
            // now all expanded -> collapse everything
            expect(model.activeCat!.subCats.every(s => s.isExpanded)).toBe(false);
            expect(model.activeCat!.eventSinks.every(s => s.isExpanded)).toBe(false);
        });

        it('derives the main message and css class from the active process', () => {
            const {model} = makeModel();

            expect(model.mainMessage).toBe('');
            expect(model.mainMessageClass).toBe('');

            // mainMessage reads the DiagProcess.title getter, so use a real instance.
            model.activeProcess = new DiagProcess(proc('p-1', 'Worker', 'Online', 'SRV01', 'svc'));

            expect(model.mainMessage).toBe('SRV01/svc/Worker');
            expect(model.mainMessageClass).toBe('title-online');
        });
    });

    describe('process subscription and property setting', () => {
        it('subscribes to a process when it is selected', async () => {
            const {model, hub} = makeModel();

            await model.selectProcess(proc('p-1', 'Worker'));

            expect(model.activeProcess?.id).toBe('p-1');
            expect(hub.connection.invoke).toHaveBeenCalledWith('Subscribe', 'p-1');
        });

        it('opens an error dialog when setPropertyValue returns an error message', async () => {
            const hub = makeHub();
            hub.setPropertyValue.mockResolvedValue({errorMessage: 'Denied'});
            const {model, dialog, snackBar} = makeModel(hub);
            model.activeProcess = proc('p-1', 'Worker');

            await model.setPropertyValue({getPropertyPath: () => 'Config.Timeout'} as any, '15');

            expect(dialog.open).toHaveBeenCalled();
            expect(snackBar.open).not.toHaveBeenCalled();
        });

        it('opens an error dialog when setPropertyValue throws', async () => {
            const hub = makeHub();
            hub.setPropertyValue.mockRejectedValue(new Error('network'));
            const {model, dialog, snackBar} = makeModel(hub);
            model.activeProcess = proc('p-1', 'Worker');

            await model.setPropertyValue({getPropertyPath: () => 'Config.Timeout'} as any, '15');

            expect(dialog.open).toHaveBeenCalled();
            expect(snackBar.open).not.toHaveBeenCalled();
        });

        it('confirms with a snackbar when setPropertyValue succeeds', async () => {
            const hub = makeHub();
            hub.setPropertyValue.mockResolvedValue({});
            const {model, dialog, snackBar} = makeModel(hub);
            model.activeProcess = proc('p-1', 'Worker');

            await model.setPropertyValue({getPropertyPath: () => 'Config.Timeout'} as any, '15');

            expect(snackBar.open).toHaveBeenCalledWith('Property set!', '', expect.any(Object));
            expect(dialog.open).not.toHaveBeenCalled();
        });

        it('asks the hub to delete a process', async () => {
            const {model, hub} = makeModel();

            await model.deleteProcess(proc('p-1', 'Worker'));

            expect(hub.removeProcess).toHaveBeenCalledWith('p-1');
        });

        it('shows an error dialog when deleting a process fails', async () => {
            const hub = makeHub();
            hub.removeProcess.mockRejectedValue(new Error('nope'));
            const {model, dialog} = makeModel(hub);

            await model.deleteProcess(proc('p-1', 'Worker'));

            expect(dialog.open).toHaveBeenCalled();
        });
    });

    describe('severity polling', () => {
        it('polls every category for severity decay once started', async () => {
            jest.useFakeTimers();
            try {
                const {model} = makeModel();
                const cat = {checkEventSeverityLevels: jest.fn()};
                model.categories = [cat as any];

                await model.start();
                jest.advanceTimersByTime(1_000);

                expect(cat.checkEventSeverityLevels).toHaveBeenCalled();
                model.severityCheckSubscription?.unsubscribe();
            } finally {
                jest.useRealTimers();
            }
        });
    });
});
