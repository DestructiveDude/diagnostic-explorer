import {Injectable} from '@angular/core';
import {getErrorMessage, today} from '../util/util';
import {RetroQuery, RetroSearchResult} from './RetroQuery';
import {DatePipe} from '@angular/common';
import {DiagnosticMsg} from './DiagnosticMsg';
import {FilterCriteria} from './FilterCriteria';
import * as _ from 'lodash';
import {Watch} from '../util/Watch';
import {DiagHubService} from '../services/diag-hub.service';
import {plainToInstance} from 'class-transformer';
import {MatSnackBar} from '@angular/material/snack-bar';
import {DiagProcess} from './DiagProcess';

@Injectable()
export class RetroModel {

    constructor(readonly datePipe: DatePipe, readonly hubService: DiagHubService, readonly snackBar: MatSnackBar) {
        this.reset();
        this.watchEnabled = true;

        this.hubService.connectionReady.subscribe(connection => {
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
                    this.snackBar.open(error, '', {duration: 2_000});
                }
            });
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

        await this.search();
    }

    async search(): Promise<void> {
        if (this.currentSearchId) {
            const searchId = this.currentSearchId;
            this.onSearchComplete(true, false);
            await this.hubService.cancelRetroSearch(searchId);
            return;
        }

        this.titleMessage = 'Searching...';
        let query: RetroQuery = this.createSearchQuery();
        this.results = [];
        this.filteredResults = [];
        this.currentSearchId = ++this.searchCount;
        query.searchId = this.currentSearchId;
        this.searchStartTime = new Date();
        await this.hubService.startRetroSearch(query);
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
            this.snackBar.open(`${deleted} records deleted`, '',
                {
                    duration: 2_000,
                    panelClass: 'message-snackbar',
                    horizontalPosition: 'center',
                    verticalPosition: 'top'
                });

            await this.search();
        } catch (err) {
            console.log(err);
            this.snackBar.open(getErrorMessage(err), '',
                {
                    duration: 2000,
                    politeness: 'assertive',
                    panelClass: 'message-snackbar',
                    horizontalPosition: 'center',
                    verticalPosition: 'top'
                });
        }
    }

    public get canDelete(): boolean {
        return !this.currentSearchId && this.displayResults.length > 0;
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
