import {DatePipe} from '@angular/common';
import {RetroModel} from './RetroModel';
import {Level} from './Level';

/**
 * A fake hub connection that records the handlers RetroModel registers via
 * `connection.on(name, handler)`, so a test can fire an inbound push by
 * invoking the captured handler directly. Same pattern as RealtimeModel.spec.
 */
function makeConnection() {
    const handlers: Record<string, (...args: any[]) => void> = {};
    return {
        on: jest.fn((name: string, handler: (...args: any[]) => void) => {
            handlers[name] = handler;
        }),
        handlers,
    };
}

/**
 * A fake DiagHubService. connectionReady captures its subscriber so the test
 * can emit a connection on demand and exercise the wiring set up in the
 * model's constructor.
 */
function makeHub() {
    let readyCb: ((c: any) => void) | undefined;
    return {
        connectionReady: {subscribe: jest.fn((cb: (c: any) => void) => (readyCb = cb))},
        startRetroSearch: jest.fn().mockResolvedValue(undefined),
        cancelRetroSearch: jest.fn().mockResolvedValue(undefined),
        deleteRecords: jest.fn().mockResolvedValue(0),
        retroSupportsDelete: jest.fn().mockResolvedValue(true),
        emitReady(c: any) { readyCb?.(c); },
    };
}

function makeModel(hub = makeHub(), messages = {add: jest.fn()}) {
    const model = new RetroModel(new DatePipe('en-US'), hub as any, messages as any);
    return {model, hub, messages};
}

describe('RetroModel', () => {
    it('builds a retro query from the entered machine/process/user/message and starts it', async () => {
        const {model, hub} = makeModel();
        model.machine = 'SRV01';
        model.process = 'Worker';
        model.user = 'chris';
        model.message = 'timeout';

        await model.search();

        expect(hub.startRetroSearch).toHaveBeenCalledWith(
            expect.objectContaining({machine: 'SRV01', process: 'Worker', user: 'chris', message: 'timeout'}),
        );
        expect(model.currentSearchId).not.toBe(0);
    });

    it('cancels the in-flight search instead of starting a new one', async () => {
        const {model, hub} = makeModel();
        model.currentSearchId = 5;
        model.searchStartTime = new Date();

        await model.search();

        expect(hub.cancelRetroSearch).toHaveBeenCalledWith(5);
        expect(hub.startRetroSearch).not.toHaveBeenCalled();
    });

    it('reports filtered counts in resultsMessage when a filter is active', () => {
        const {model} = makeModel();
        model.results = [
            {level: Level.ERROR, message: 'Timeout', detail: '', msgId: '1'} as any,
            {level: Level.ERROR, message: 'Connected', detail: '', msgId: '2'} as any,
        ];
        model.filterVisible = true;
        model.filterCriteria.searchText = 'time';

        (model as any).filterResults();

        expect(model.displayResults).toHaveLength(1);
        expect(model.resultsMessage).toBe('1 of 2 events');
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

    describe('delete', () => {
        const realConfirm = globalThis.confirm;
        afterEach(() => {
            globalThis.confirm = realConfirm;
        });

        it('deletes the displayed records and reports the count after confirmation', async () => {
            const hub = makeHub();
            hub.deleteRecords.mockResolvedValue(2);
            const {model, messages} = makeModel(hub);
            globalThis.confirm = jest.fn().mockReturnValue(true);
            model.results = [{msgId: '1'}, {msgId: '2'}] as any;

            await model.delete();

            expect(hub.deleteRecords).toHaveBeenCalledWith(['1', '2']);
            expect(messages.add).toHaveBeenCalledWith(expect.objectContaining({ detail: '2 records deleted' }));
        });

        it('does nothing when the user cancels the confirmation', async () => {
            const hub = makeHub();
            const {model, messages} = makeModel(hub);
            globalThis.confirm = jest.fn().mockReturnValue(false);
            model.results = [{msgId: '1'}] as any;

            await model.delete();

            expect(hub.deleteRecords).not.toHaveBeenCalled();
            expect(messages.add).not.toHaveBeenCalled();
        });
    });

    describe('canDelete gating', () => {
        it('allows delete on a backend that supports it once results are present', () => {
            const {model} = makeModel();
            model.supportsDelete = true;
            model.results = [{msgId: '1'}] as any;

            expect(model.canDelete).toBe(true);
        });

        it('blocks delete on an append-only backend even with results present', () => {
            const {model} = makeModel();
            model.supportsDelete = false;
            model.results = [{msgId: '1'}] as any;

            expect(model.canDelete).toBe(false);
        });

        it('queries the backend delete capability when the connection becomes ready', async () => {
            const hub = makeHub();
            hub.retroSupportsDelete.mockResolvedValue(false);

            const {model} = makeModel(hub);
            hub.emitReady(makeConnection());
            await Promise.resolve();
            await Promise.resolve();

            expect(hub.retroSupportsDelete).toHaveBeenCalled();
            expect(model.supportsDelete).toBe(false);
        });
    });

    describe('SignalR wiring', () => {
        it('registers the three retro push handlers on connectionReady', () => {
            const {hub} = makeModel();
            const connection = makeConnection();

            hub.emitReady(connection);

            expect(Object.keys(connection.handlers).sort()).toEqual([
                'ProcessSearchEnd', 'ProcessSearchError', 'ProcessSearchResults',
            ]);
        });

        it('routes ProcessSearchResults to appendResponse for the active search', () => {
            const {model, hub} = makeModel();
            const connection = makeConnection();
            hub.emitReady(connection);
            model.currentSearchId = 7;
            model.searchStartTime = new Date();

            connection.handlers['ProcessSearchResults']({
                searchId: 7,
                results: [{msgId: 'm-1', message: 'Timeout'}],
            });

            expect(model.results).toHaveLength(1);
            expect(model.titleMessage).toBe('Searching... 1 records');
            expect(model.resultsMessage).toBe('1 events');
        });

        it('ignores ProcessSearchResults for a different search id', () => {
            const {model, hub} = makeModel();
            const connection = makeConnection();
            hub.emitReady(connection);
            model.currentSearchId = 7;
            model.searchStartTime = new Date();

            connection.handlers['ProcessSearchResults']({
                searchId: 99,
                results: [{msgId: 'm-1', message: 'Timeout'}],
            });

            expect(model.results).toHaveLength(0);
        });

        it('routes ProcessSearchEnd to onSearchComplete for the active search', () => {
            const {model, hub} = makeModel();
            const connection = makeConnection();
            hub.emitReady(connection);
            model.currentSearchId = 7;
            model.searchStartTime = new Date();

            connection.handlers['ProcessSearchEnd'](7);

            expect(model.currentSearchId).toBe(0);
            expect(model.titleMessage).toMatch(/^Search complete in /);
        });

        it('routes ProcessSearchError to a snackbar and clears the active search', () => {
            const {model, hub, messages} = makeModel();
            const connection = makeConnection();
            hub.emitReady(connection);
            model.currentSearchId = 7;
            model.searchStartTime = new Date();

            connection.handlers['ProcessSearchError'](7, 'boom', 'detail');

            expect(model.currentSearchId).toBe(0);
            expect(model.titleMessage).toBe('Search failed: boom');
            expect(messages.add).toHaveBeenCalledWith(expect.objectContaining({severity: 'error', detail: 'boom'}));
        });

        it('ignores ProcessSearchEnd and ProcessSearchError for a different search id', () => {
            const {model, hub, messages} = makeModel();
            const connection = makeConnection();
            hub.emitReady(connection);
            model.currentSearchId = 7;
            model.searchStartTime = new Date();

            connection.handlers['ProcessSearchEnd'](99);
            connection.handlers['ProcessSearchError'](99, 'boom', 'detail');

            expect(model.currentSearchId).toBe(7);
            expect(messages.add).not.toHaveBeenCalled();
        });
    });
});
