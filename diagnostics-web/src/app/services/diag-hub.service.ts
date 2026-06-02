import {Inject, Injectable} from '@angular/core';
import {HubConnection} from '@microsoft/signalr';
import * as signalR from '@microsoft/signalr';
import {ReplaySubject} from 'rxjs';
import {OperationResponse, SetPropertyRequest} from '../Model/SetPropertyRequest';
import {plainToInstance} from 'class-transformer';
import {ExecOperationRequest} from '../Model/ExecOperationRequest';
import {RetroQuery} from '../Model/RetroQuery';
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
                    document.cookie = cookieString;
                }

                const connection = new signalR.HubConnectionBuilder()
                    .withUrl(this.baseUrl, {
                        withCredentials: true
                    })
                    .build();

                connection.onreconnecting(err => this.handleConnectionClosed(err));
                connection.onclose(err => this.handleConnectionClosed(err));
                await connection.start();

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
        const response = await this.connection!.invoke<OperationResponse>(`SetProperty`, request);
        return plainToInstance(OperationResponse, response);
    }

    async executeOperation(request: ExecOperationRequest): Promise<OperationResponse> {
        const response = await this.connection!.invoke<OperationResponse>(`ExecuteOperation`, request);
        return plainToInstance(OperationResponse, response);
    }

    async removeProcess(id: string): Promise<void> {
        await this.connection!.invoke('RemoveProcess', id);
    }

    async startRetroSearch(query: RetroQuery): Promise<void> {
        await this.connection!.invoke('StartRetroSearch', query);
    }

    async cancelRetroSearch(searchId: number): Promise<void> {
        await this.connection!.invoke('CancelRetroSearch', searchId);
    }

    private async handleConnectionClosed(err: Error | undefined) {
        this.connection = undefined;
        await this.connect();
    }

    async deleteRecords(toDelete: string[]): Promise<number> {
        return await this.connection!.invoke<number>('RetroDelete', toDelete);
    }
}
