import {Injectable} from '@angular/core';
import {getErrorMessage, today} from '../util/util';
import {RetroQuery, RetroSearchResult} from './RetroQuery';
import {DatePipe} from '@angular/common';
import {DiagnosticMsg} from './DiagnosticMsg';
import {FilterCriteria} from './FilterCriteria';
import _ from 'lodash';
import {Watch} from '../util/Watch';
import {DiagHubService} from '../services/diag-hub.service';
import {plainToInstance} from 'class-transformer';
import {MessageService} from 'primeng/api';
import {DiagProcess} from './DiagProcess';

@Injectable()
export class RetroModel {

    constructor(readonly datePipe: DatePipe, readonly hubService: DiagHubService, readonly messages: MessageService) {
        this.reset();
        this.watchEnabled = true;

        this.hubService.connectionReady.subscribe(connection => {
            if (this.currentSearchId) {
                this.onSearchComplete(true, false);
                this.titleMessage = 'Search interrupted by reconnect';
                this.messages.add({ severity: 'warn', detail: 'Search interrupted by reconnect', life: 3000 });
            }

            connection.on("ProcessSearchResults", (result: RetroSearchResult) => {
                if (this.currentSearchId === result.searchId)
                    this.appendResponse(plainToInstance(RetroSearchResult, result));
            });
            connection.on("ProcessSearchEnd", (searchId: number) => {
                if (this.currentSearchId === searchId)
                    this.onSearchComplete(false, false);
            });
            connection.on("ProcessSearchError", (searchId: number, error: string, detail: string) => {
                console.log(error);
                if (this.currentSearchId === searchId) {
                    // Reset the active-search state, otherwise currentSearchId stays set and the UI is
                    // stuck on "Searching..." — the next search() call would route to the cancel branch
                    // and canDelete would never re-enable.
                    this.onSearchComplete(true, false);
                    this.titleMessage = `Search failed: ${error}`;
                    this.messages.add({ severity: 'error', detail: error, life: 2000 });
                }
            });

            // Append-only backends (Log Analytics) cannot delete; hide the affordance once the
            // backend reports its capability. Re-queried on every (re)connect — harmless.
            this.hubService.retroSupportsDelete()
                .then(supported => this.supportsDelete = supported)
                .catch(err => console.log(err));
        });
    }


    maxRecords = 5000;
    minLevel = 0;
    date = today();
    time = 0;
    hours = 1;
    machine = '';
    process = '';
    user = '';
    message = '';
    percentComplete = 0;
    searchesTotal = 0;
    titleMessage = '';
    resultsMessage = '';
    searchStartTime?: Date;
    mainMessage = 'Retro Diagnostics';
    mainMessageClass = '';
    mainMessageClick = _.noop;
    searchCount = 0;
    currentSearchId = 0;

    // Defaults true so the default (MongoDB) backend keeps delete enabled before the capability
    // query resolves; set false for append-only backends (Log Analytics) — see canDelete.
    supportsDelete = true;

    results: DiagnosticMsg[] = [];
    filteredResults: DiagnosticMsg[] = [];
    selectedEvent?: DiagnosticMsg;
    traceScopeVisible = false;
    watchEnabled = false;

    @Watch((_this: RetroModel) => _this.filterResults())
    filterVisible = false;

    @Watch((_this: RetroModel) => _this.filterResults())
    filterCriteria = new FilterCriteria();

    get displayResults(): DiagnosticMsg[] {
        return this.filterActive
            ? this.filteredResults
            : this.results;
    }

    get filterActive(): boolean {
        return this.filterVisible && !this.filterCriteria.isBlank;
    }


    reset(): void {
        this.maxRecords = 5000;
        this.minLevel = 0;
        this.date = today();
        this.time = 7;//new Date().getHours();
        this.hours = 12;
        this.machine = '';
        this.process = '';
        this.user = '';
        this.message = '';
    }

    public async searchProcess(item: DiagProcess): Promise<void> {
        this.reset();
        this.process = item.processName;
        this.user = item.userName;
        this.machine = item.machineName;

        this.time = new Date().getHours() - 1;
        this.hours = 2;

        if (this.currentSearchId) {
            const searchId = this.currentSearchId;
            this.onSearchComplete(true, false);
            try {
                await this.hubService.cancelRetroSearch(searchId);
            } catch (err) {
                console.log(err);
            }
        }

        await this.search();
    }

    async search(): Promise<void> {
        if (this.currentSearchId) {
            const searchId = this.currentSearchId;
            this.onSearchComplete(true, false);
            try {
                await this.hubService.cancelRetroSearch(searchId);
            } catch (err) {
                console.log(err);
            }
            return;
        }

        this.titleMessage = 'Searching...';
        this.percentComplete = 0;
        let query: RetroQuery = this.createSearchQuery();
        this.results = [];
        this.filteredResults = [];
        this.currentSearchId = ++this.searchCount;
        query.searchId = this.currentSearchId;
        this.searchStartTime = new Date();
        try {
            await this.hubService.startRetroSearch(query);
        } catch (err) {
            console.log(err);
            this.onSearchComplete(true, false);
            this.titleMessage = `Search failed to start: ${getErrorMessage(err)}`;
            this.messages.add({ severity: 'error', detail: getErrorMessage(err), life: 3000 });
        }
    }

    async delete(): Promise<void> {
        if (this.currentSearchId)
            return;

        try {
            const toDelete = this.displayResults.map(m => m.msgId);

            const msg = `Are you sure you want to delete ${toDelete.length} log entries?`;
            if (!confirm(msg))
                return;

            const deleted = await this.hubService.deleteRecords(toDelete);
            this.messages.add({ severity: 'success', detail: `${deleted} records deleted`, life: 2000 });

            await this.search();
        } catch (err) {
            console.log(err);
            this.messages.add({ severity: 'error', detail: getErrorMessage(err), life: 2000 });
        }
    }

    public get canDelete(): boolean {
        return this.supportsDelete && !this.currentSearchId && this.displayResults.length > 0;
    }


    private createSearchQuery(): RetroQuery {
        const search = new RetroQuery();
        search.maxRecords = this.maxRecords;
        search.minLevel = this.minLevel;
        search.machine = this.machine;
        search.process = this.process;
        search.machine = this.machine;
        search.user = this.user;
        search.message = this.message;

        // Copy before mutating: this.date.setHours(...) would mutate the bound date-picker
        // field in place, so the UI date would jump to the chosen hour after a search.
        const start = new Date(this.date);
        start.setHours(this.time, 0, 0, 0);
        search.startDate = start.toISOString();
        search.endDate = new Date(start.valueOf() + this.hours * 60 * 60 * 1000).toISOString();

        return search;
    }

    private appendResponse(searchResult: RetroSearchResult) {
        try {
            if (searchResult.searchId !== this.currentSearchId)
                return;

            const results = searchResult.results;
            if (results.length) {
                this.results = this.results.concat(results);
                if (this.filterActive) {
                    const filtered = results.filter(evt => this.filterCriteria.filter(evt));
                    if (filtered.length)
                        this.filteredResults = this.filteredResults.concat(filtered);
                }
            }

            this.percentComplete += (1 - this.percentComplete) / 100;

            this.initResultsMessage();
            this.titleMessage = `Searching... ${this.results.length} records`;

            // if (this.percentComplete === 100)
            //   this.onSearchComplete(false, false);
        } catch (err) {
            console.log(err);
        }
    }

    private onSearchComplete(cancelled = false, limitReached = false) {
        const millis = new Date().valueOf() - this.searchStartTime!.valueOf();
        const time = millis > 1000
            ? (millis / 1000).toFixed(2) + 's'
            : millis + 'ms';

        this.searchStartTime = undefined;
        this.currentSearchId = 0;
        console.log('OnSearchComplete 1', this.currentSearchId);

        this.titleMessage = cancelled
            ? `Search cancelled after ${time}`
            : limitReached
                ? `Record limit reached after ${time}`
                : `Search complete in ${time}`;

        this.initResultsMessage();
    }

    handleMouseOver(item: DiagnosticMsg, evt: MouseEvent) {
        if (evt.buttons === 1)
            this.setCurrentEvent(item);
    }

    setCurrentEvent(item: DiagnosticMsg) {
        if (this.selectedEvent)
            this.selectedEvent.isSelected = false;

        this.selectedEvent = item;
        this.selectedEvent.isSelected = true;
        this.traceScopeVisible = true;
    }

    private initResultsMessage(): void {
        this.resultsMessage = this.filterActive
            ? `${this.filteredResults.length} of ${this.results.length} events`
            : `${this.results.length} events`;
    }

    private filterResults(): void {
        if (this.filterActive)
            this.filteredResults = this.results.filter(item => this.filterCriteria.filter(item));
        else
            this.filteredResults = [];

        this.initResultsMessage();
    }

    hideTraceScope(): void {
        this.traceScopeVisible = false;
    }

}
