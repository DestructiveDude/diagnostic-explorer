import {Inject, Injectable} from '@angular/core';
import {HubConnection, HubConnectionBuilder} from '@microsoft/signalr';
import {ReplaySubject} from 'rxjs';
import {OperationResponse, SetPropertyRequest} from '../Model/SetPropertyRequest';
import {plainToInstance} from 'class-transformer';
import {ExecOperationRequest} from '../Model/ExecOperationRequest';
import {RetroQuery} from '../Model/RetroQuery';
import {DrillDownRequest, DrillDownResponse} from '../Model/DrillDownRequest';
import {BASE_API_URL, BASE_API_KEY} from "../../injectionTokens";

@Injectable({
    providedIn: 'root'
})
export class DiagHubService {

    public connection?: HubConnection;
    public connectionReady = new ReplaySubject<HubConnection>(1);
    public connectionStarted = new ReplaySubject<HubConnection>(1);
    private connecting = false;


    constructor(
        @Inject(BASE_API_URL) private baseUrl: string,
        @Inject(BASE_API_KEY) private apiKey: string) {
    }

    public async connect(): Promise<void> {
        // Guard against concurrent reconnect loops: if handleConnectionClosed fires while a
        // connect() is already in its retry delay, the second call returns immediately and the
        // existing loop continues (this.connection is already undefined, so the while condition
        // remains true and the existing loop reconnects).
        if (this.connecting) return;
        this.connecting = true;
        try {
        while (!this.connection) {
            try {

                // H1: Set a short-lived cookie containing the API key (if configured) so that both the
                // negotiate request and the WebSocket upgrade request securely send it without exposing
                // the API key in the query string.
                if (this.apiKey) {
                    let cookieString = `Diag-Hub-Auth=${encodeURIComponent(this.apiKey)}; path=/; max-age=60; SameSite=Strict`;
                    if (window.location.protocol === 'https:') {
                        cookieString += '; Secure';
                    }
                    // eslint-disable-next-line sonarjs/cookies -- deliberate H1 design: 60s, SameSite=Strict, Secure on https; cleared below.
                    document.cookie = cookieString;
                }

                const connection = new HubConnectionBuilder()
                    .withUrl(this.baseUrl, {
                        withCredentials: true,
                        accessTokenFactory: () => this.apiKey
                    })
                    .build();

                connection.onreconnecting(err => this.handleConnectionClosed(err));
                connection.onclose(err => this.handleConnectionClosed(err));
                await connection.start();

                // Clear the short-lived auth cookie once successfully connected.
                if (this.apiKey) {
                    // eslint-disable-next-line sonarjs/cookies -- expiry write for the H1 cookie above.
                    document.cookie = 'Diag-Hub-Auth=; path=/; max-age=0; SameSite=Strict';
                }

                // Assign this.connection BEFORE emitting: subscribers (e.g. RealtimeModel's
                // connectionStarted handler) call this.connection.invoke('Subscribe', ...). If the
                // field were still undefined at emit time the re-subscribe would silently no-op,
                // so after a reconnect the client would stop receiving realtime diagnostics.
                this.connection = connection;
                this.connectionReady.next(connection);
                this.connectionStarted.next(connection);
            } catch (err) {
                console.log(err);
                await new Promise(resolve => setTimeout(resolve, 1000));
            }
        }
        } finally {
            this.connecting = false;
        }
    }

    async setPropertyValue(request: SetPropertyRequest): Promise<OperationResponse> {
        // await the RPC: passing the un-awaited Promise to plainToInstance produced a default
        // OperationResponse (isSuccess:false, empty errorMessage), so callers saw "Property set!"
        // even when the hub returned an error.
        if (!this.connection) {
            return { isSuccess: false, errorMessage: "Not connected to service" } as OperationResponse;
        }
        const response = await this.connection.invoke<OperationResponse>(`SetProperty`, withRequestId(request));
        return plainToInstance(OperationResponse, response);
    }

    async executeOperation(request: ExecOperationRequest): Promise<OperationResponse> {
        if (!this.connection) {
            return { isSuccess: false, errorMessage: "Not connected to service" } as OperationResponse;
        }
        const response = await this.connection.invoke<OperationResponse>(`ExecuteOperation`, withRequestId(request));
        return plainToInstance(OperationResponse, response);
    }

    /**
     * Opens the value the request names. Errors come back on the response rather than as a
     * rejection: a path that no longer resolves — an object replaced between poll and click — is
     * an ordinary outcome, not a transport fault.
     */
    async getDrillDown(request: DrillDownRequest): Promise<DrillDownResponse> {
        if (!this.connection) {
            return {...new DrillDownResponse(), errorMessage: 'Not connected to service'};
        }
        const response = await this.connection.invoke<DrillDownResponse>('GetDrillDown', request);
        return plainToInstance(DrillDownResponse, response);
    }

    async removeProcess(id: string): Promise<void> {
        if (!this.connection) return;
        await this.connection.invoke('RemoveProcess', id);
    }

    async startRetroSearch(query: RetroQuery): Promise<void> {
        if (!this.connection) {
            throw new Error("Not connected to service");
        }
        await this.connection.invoke('StartRetroSearch', query);
    }

    async cancelRetroSearch(searchId: number): Promise<void> {
        if (!this.connection) return;
        await this.connection.invoke('CancelRetroSearch', searchId);
    }

    private async handleConnectionClosed(_err: Error | undefined) {
        this.connection = undefined;
        await this.connect();
    }

    async deleteRecords(toDelete: string[]): Promise<number> {
        if (!this.connection) return 0;
        return await this.connection.invoke<number>('RetroDelete', toDelete);
    }

    async retroSupportsDelete(): Promise<boolean> {
        if (!this.connection) return false;
        return await this.connection.invoke<boolean>('RetroSupportsDelete');
    }
}

/**
 * Stamps an action with the id the agent deduplicates on, leaving one already set alone.
 *
 * The service gives up on a slow action long before the agent does, so a timed-out action is
 * still running when the operator is told it failed. Retrying that action must therefore reuse
 * its id - a caller that mints a fresh one is asking for the body to run a second time, which
 * for an operation is exactly the damage the id exists to prevent. A brand new gesture is a new
 * intent and correctly gets a new id.
 */
function withRequestId<T extends { requestId: string }>(request: T): T {
    return request.requestId ? request : {...request, requestId: newRequestId()};
}

function newRequestId(): string {
    // randomUUID needs a secure context; the fallback keeps a plain-http deployment working
    // rather than silently sending an empty id, which the agent treats as "run unguarded".
    const cryptoApi = globalThis.crypto as Crypto | undefined;
    if (cryptoApi?.randomUUID) return cryptoApi.randomUUID();
    return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}
