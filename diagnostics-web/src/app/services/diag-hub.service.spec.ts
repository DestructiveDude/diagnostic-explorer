import * as signalR from '@microsoft/signalr';
import {firstValueFrom} from 'rxjs';
import {DiagHubService} from './diag-hub.service';

interface FakeConnection {
    start: jest.Mock;
    invoke: jest.Mock;
    onreconnecting: jest.Mock;
    onreconnected: jest.Mock;
    onclose: jest.Mock;
}

function fakeConnection(startImpl?: jest.Mock): FakeConnection {
    return {
        start: startImpl ?? jest.fn().mockResolvedValue(undefined),
        invoke: jest.fn().mockResolvedValue(undefined),
        onreconnecting: jest.fn(),
        onreconnected: jest.fn(),
        onclose: jest.fn(),
    };
}

function stubBuilder(...connections: FakeConnection[]): jest.Mock {
    const build = jest.fn();
    connections.forEach(c => build.mockReturnValueOnce(c));
    const withUrl = jest.fn().mockReturnThis();
    return jest.spyOn(signalR, 'HubConnectionBuilder').mockImplementation(
        () => ({withUrl, build}) as any,
    ) as unknown as jest.Mock;
}

describe('DiagHubService', () => {
    afterEach(() => jest.restoreAllMocks());

    describe('connect', () => {
        it('builds the connection, starts it, and publishes it to both subjects', async () => {
            const connection = fakeConnection();
            stubBuilder(connection);
            const service = new DiagHubService('/diagnostics', '');

            const ready = firstValueFrom(service.connectionReady);
            const started = firstValueFrom(service.connectionStarted);

            await service.connect();

            await expect(ready).resolves.toBe(connection as any);
            await expect(started).resolves.toBe(connection as any);
            expect(connection.start).toHaveBeenCalledTimes(1);
            expect(service.connection).toBe(connection as any);
            // lifecycle handlers wired exactly once
            expect(connection.onreconnecting).toHaveBeenCalledTimes(1);
            expect(connection.onclose).toHaveBeenCalledTimes(1);
        });

        it('retries until a connection starts successfully', async () => {
            const failing = fakeConnection(jest.fn().mockRejectedValue(new Error('refused')));
            const working = fakeConnection();
            stubBuilder(failing, working);
            const service = new DiagHubService('/diagnostics', '');

            await service.connect();

            expect(failing.start).toHaveBeenCalledTimes(1);
            expect(working.start).toHaveBeenCalledTimes(1);
            expect(service.connection).toBe(working as any);
        });
    });

    describe('hub invocations', () => {
        let service: DiagHubService;
        let connection: FakeConnection;

        beforeEach(() => {
            service = new DiagHubService('/diagnostics', '');
            connection = fakeConnection();
            service.connection = connection as any;
        });

        it('invokes SetProperty with the supplied request, stamped with a request id', async () => {
            const request = {processId: 'p-1', propertyPath: 'Config.Timeout', value: '15'} as any;
            await service.setPropertyValue(request);
            expect(connection.invoke).toHaveBeenCalledWith('SetProperty', {
                ...request,
                requestId: expect.any(String),
            });
        });

        it('invokes ExecuteOperation with the supplied request, stamped with a request id', async () => {
            const request = {processId: 'p-1', operation: 'Restart'} as any;
            await service.executeOperation(request);
            expect(connection.invoke).toHaveBeenCalledWith('ExecuteOperation', {
                ...request,
                requestId: expect.any(String),
            });
        });

        // The id identifies the operator's action, so a caller that already has one - a retry of
        // an action the service timed out on - must keep it. Minting a fresh one there is exactly
        // the double-run the agent's guard exists to refuse.
        it('keeps a request id the caller already set', async () => {
            const request = {processId: 'p-1', operation: 'Restart', requestId: 'attempt-1'} as any;
            await service.executeOperation(request);
            expect(connection.invoke).toHaveBeenCalledWith('ExecuteOperation', request);
        });

        it('gives two separate actions different request ids', async () => {
            await service.executeOperation({processId: 'p-1', operation: 'Restart'} as any);
            await service.executeOperation({processId: 'p-1', operation: 'Restart'} as any);

            const [first, second] = connection.invoke.mock.calls.map((call: any[]) => call[1].requestId);
            expect(first).toBeTruthy();
            expect(second).not.toBe(first);
        });

        it('invokes RemoveProcess, StartRetroSearch and CancelRetroSearch', async () => {
            const query = {searchId: 7} as any;
            await service.removeProcess('p-1');
            await service.startRetroSearch(query);
            await service.cancelRetroSearch(7);

            expect(connection.invoke).toHaveBeenCalledWith('RemoveProcess', 'p-1');
            expect(connection.invoke).toHaveBeenCalledWith('StartRetroSearch', query);
            expect(connection.invoke).toHaveBeenCalledWith('CancelRetroSearch', 7);
        });

        it('returns the deleted-record count from RetroDelete', async () => {
            connection.invoke.mockResolvedValue(3);
            await expect(service.deleteRecords(['m-1', 'm-2'])).resolves.toBe(3);
            expect(connection.invoke).toHaveBeenCalledWith('RetroDelete', ['m-1', 'm-2']);
        });
    });
});
