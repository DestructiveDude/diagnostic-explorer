import {DiagProcess} from './DiagProcess';
import {Subscription, timer} from 'rxjs';
import {Null} from '../util/Null';
import {Watch} from '../util/Watch';
import {DiagnosticResponse, OperationSet, PropertyBag, SystemEvent} from './DiagResponse';
import * as _ from 'lodash';
import {escapeRegExp} from 'lodash';
import {customMerge, simpleMerge} from '../util/Merge';
import {Injectable} from '@angular/core';
import {CategoryModel} from './CategoryModel';
import {EventModel} from './EventModel';
import {PropModel} from './PropModel';
import {SetPropertyRequest} from './SetPropertyRequest';
import {MatDialog} from '@angular/material/dialog';
import {InfoDialogComponent} from '../info-dialog/info-dialog.component';
import {MatSnackBar} from '@angular/material/snack-bar';
import {plainToInstance} from 'class-transformer';
import {DiagHubService} from '../services/diag-hub.service';
import {DatePipe} from '@angular/common';
import {Level} from './Level';
import {strEqCI} from '../util/util';

@Injectable()
export class RealtimeModel {

    allProcesses: DiagProcess[] = [];
    filteredProcesses: DiagProcess[] = [];
    traceScopeVisible = false;

    activeProcess: DiagProcess | null = null;
    tabIndex = 0;
    titleMessage = '';
    selectedEvent?: EventModel;

    categories: CategoryModel[] = [];
    operationSets: OperationSet[] = [];
    severityCheckSubscription?: Subscription;

    @Watch((_this: RealtimeModel) => _this.performProcessSearch())
    processSearch: Null<string> = null;
    watchEnabled = false;

    @Watch((_this: RealtimeModel) => _this.performProcessSearch())
    onlineOnly = true;
    activeCat?: CategoryModel;
    selectedIndex = 0;

    constructor(readonly hubService: DiagHubService,
                readonly datePipe: DatePipe,
                private dialog: MatDialog,
                readonly snackBar: MatSnackBar) {
        this.watchEnabled = true;
        this.hubService.connectionReady.subscribe(connection => {
            connection.on('SetProcesses', (data: DiagProcess[]) => {
                this.displayProcesses(plainToInstance(DiagProcess, data) as unknown as DiagProcess[]);
            });
            connection.on('UpdateProcess', (data: DiagProcess) => {
                this.updateProcess(plainToInstance(DiagProcess, data));
            });
            connection.on('RemoveProcess', (id: string) => {
                this.removeProcess(id);
            });
            // Guard on id: a frame still in flight for a previously-selected process must not
            // overwrite the currently-selected process's view after the user switches.
            connection.on('ShowDiagnostics', (id: string, response: DiagnosticResponse) => {
                if (id === this.activeProcess?.id)
                    this.displayRealtimeDiags(response);
            });
            connection.on('ShowDiagnosticsError', (id: string, message: string) => {
                if (id === this.activeProcess?.id)
                    this.snackBar.open(message, '', {duration: 2_000});
            });
            connection.on('SetEvents', (id: string, events: SystemEvent[]) => {
                if (id === this.activeProcess?.id)
                    this.setEvents(id, events);
            });
            connection.on('StreamEvents', (id: string, evts: SystemEvent[]) => {
                if (id === this.activeProcess?.id)
                    this.streamEvents(id, evts);
            });
        });

        this.hubService.connectionStarted.subscribe(connection => {
            this.subscribeToActiveProcess();
        });
    }

    viewRealtime() {
        this.tabIndex = 0;
    }

    viewRetro() {
        this.tabIndex = 1;
    }

    async start(): Promise<void> {
        this.severityCheckSubscription = timer(0, 1_000)
            .subscribe(folder => this.checkEventSeverityLevels());

    }

    async selectProcess(process: DiagProcess) {
        this.activeProcess = process;
        this.categories = [];
        this.operationSets = [];
        this.selectedEvent = undefined;
        this.activeCat = undefined;
        this.selectedIndex = 0;

        this.titleMessage = '';
        await this.subscribeToActiveProcess();
    }

    private async subscribeToActiveProcess() {
        if (this.activeProcess)
            await this.hubService.connection?.invoke("Subscribe", this.activeProcess.id);
    }

    private displayRealtimeDiags(response: DiagnosticResponse) {
        this.titleMessage = 'Received at ' + this.datePipe.transform(new Date(), 'HH:mm:ss');

        const bagCats: { [key: string]: PropertyBag[] }
            = _(response.propertyBags).groupBy(p => p.category).value();

        const catData: { name: string, props: PropertyBag[] }[]
            = _(bagCats).keys().concat(this.categories.map(c => c.name))
            .uniq()
            .map(name => ({name, props: bagCats[name] ?? []}))
            .value();

        let cats = this.categories.slice();

        customMerge(catData,
            cats,
            d => d.name,
            c => c.name,
            d => new CategoryModel(this, d.name, d.props),
            (d, c) => c.update(d.props),
            false);

        cats = _.sortBy(cats, c => c.name);


        if (cats.filter(c => !c.subCats.length && !c.eventSinks.length))
            cats = cats.filter(c => c.subCats.length || c.eventSinks.length);

        this.categories = cats;

        this.operationSets = response.operationSets;
    }

    get mainMessage(): string {
        return this.activeProcess?.title ?? '';
    }

    get mainMessageClass(): string {
        if (!this.activeProcess)
            return '';

        return 'title-' + this.activeProcess?.state?.toLocaleLowerCase();
    }

    mainMessageClick = () => this.expandCollapse();

    //region process list

    private performProcessSearch(): void {

        if (this.processSearch || this.onlineOnly) {
            let tester: Null<RegExp> = this.createFilterRegex();

            const matching = this.allProcesses.filter(p =>
                (!this.onlineOnly || p.state == 'Online')
                &&
                (tester == null
                    || tester.test(p.processName)
                    || tester.test(p.machineName)
                    || tester.test(p.userName))
            );

            this.filteredProcesses = this.allProcesses === this.filteredProcesses
                ? matching
                : simpleMerge(matching, this.filteredProcesses, p => p.id);

        } else {
            this.filteredProcesses = this.allProcesses;
        }
    }

    private createFilterRegex(): Null<RegExp> {
        if (!this.processSearch)
            return null;

        try {
            return new RegExp(this.processSearch, 'i');
        } catch (err) {
            return new RegExp(escapeRegExp(this.processSearch), 'i');
        }
    }

    public displayProcesses(processes: DiagProcess[]): void {
        //console.log('displayProcesses', processes);
        this.mergeProcesses(processes, true);
    }

    public updateProcess(process: DiagProcess): void {
        //console.log('updateProcess', process);
        this.mergeProcesses([process], false);
    }

    private mergeProcesses(processes: DiagProcess[], removeOthers: boolean) {
        this.allProcesses = customMerge(
            processes,
            this.allProcesses,
            p => p.id,
            p => p.id,
            p => new DiagProcess(p),
            (s, t) => t.update(s),
            removeOthers
        );
        this.allProcesses = _.orderBy(this.allProcesses, [p => p.userName, p => p.machineName, p => p.processName]);

        this.performProcessSearch();
    }

    public removeProcess(id: string) {
        this.allProcesses = this.allProcesses.filter(p => p.id !== id);
        this.filteredProcesses = this.filteredProcesses.filter(p => p.id !== id);

        // If the removed process was the one being viewed, drop the selection and its diagnostics
        // view — otherwise activeProcess still points at a gone process and SetProperty/ExecuteOperation
        // would be issued against it.
        if (this.activeProcess?.id === id) {
            this.activeProcess = null;
            this.categories = [];
            this.operationSets = [];
            this.activeCat = undefined;

            if (this.selectedEvent)
                this.selectedEvent.isSelected = false;

            this.selectedEvent = undefined;
            this.traceScopeVisible = false;
            this.titleMessage = '';
        }
    }

    handleKeyDown($event: KeyboardEvent) {
        if ($event.key === 'Escape')
            this.processSearch = null;
    }

    setCurrentEvent(item: EventModel) {
        if (this.selectedEvent)
            this.selectedEvent.isSelected = false;

        this.selectedEvent = item;
        this.selectedEvent.isSelected = true;
        this.traceScopeVisible = true;
    }

    handleMouseOver(item: EventModel, evt: MouseEvent) {
        if (evt.buttons === 1)
            this.setCurrentEvent(item);
    }

    hideTraceScope() {
        this.traceScopeVisible = false;
    }

    expandCollapse(): void {
        if (this.activeCat) {
            const expandable: { isExpanded: boolean }[] = [];
            expandable.push(...this.activeCat.subCats);
            expandable.push(...this.activeCat.eventSinks);

            const allExpanded = expandable.every(item => item.isExpanded);
            expandable.forEach(exp => exp.isExpanded = !allExpanded);
        }
    }

    handleSelectedTabChanged(index: number) {
        this.activeCat = this.categories[index];
    }

    async setPropertyValue(prop: PropModel, value: string) {
        try {
            const request = new SetPropertyRequest();
            request.id = this.activeProcess!.id;
            request.path = prop.getPropertyPath();
            request.value = value;

            const result = await this.hubService.setPropertyValue(request);
            if (result.errorMessage) {
                console.log(result);
                this.showError('Error setting property', result.errorMessage);
            } else {
                this.snackBar.open('Property set!', '', {
                    horizontalPosition: 'center',
                    verticalPosition: 'top',
                    politeness: 'assertive',
                    panelClass: 'value-copied-snackbar',
                    duration: 1_000,
                });
            }
        } catch (err: any) {
            console.log(err);
            this.showError('Error setting property', 'See console for details');
        }
    }

    private showError(title: string, message: string) {
        this.dialog.open(InfoDialogComponent, {
            data: {
                title: 'Error setting property',
                message: message
            }
        });
    }

    async deleteProcess(item: DiagProcess): Promise<void> {
        try {
            await this.hubService.removeProcess(item.id);
        } catch (err) {
            console.log(err);
            this.showError('Error setting property', 'See console for details');
        }
    }


    private setEvents(id: string, evts: SystemEvent[]): void {
        //console.log(`setEvents ${id}: ${events.length}`);

        if (this.activeProcess?.id === id) {
            this.categories.forEach(c => c.eventSinks = []);
            this.streamEvents(id, evts);
        }
    }

    private streamEvents(id: string, evts: SystemEvent[]): void {
        if (this.activeProcess?.id !== id) return;

        evts.forEach(evt => this.setEventLevel(evt));
        // Copy before reversing: evts is the array delivered by the SignalR handler; reversing it
        // in place mutates a caller-owned array.
        const ordered = [...evts].reverse();

        var grouped = _.groupBy<SystemEvent>(ordered, evt => evt.sinkCategory);
        for (const cat in grouped)
            this.getCat(cat).addEvents(grouped[cat]);
    }

    private setEventLevel(evt: SystemEvent): void {
        // The server always populates level (log4net Level.Value), so this fallback only fires for
        // an event that arrives without one — default it to ERROR so it stays visible. (Previously
        // derived from evt.severity, which the server never sent; that dead field has been removed.)
        if (!evt.level)
            evt.level = Level.ERROR;
    }

    private getCat(name: string): CategoryModel {
        let cat = this.categories.find(c => strEqCI(c.name, name));
        if (!cat) {
            cat = new CategoryModel(this, name);
            this.categories = _.sortBy(this.categories.concat(cat), c => c.name);
        }

        return cat;
    }

    private checkEventSeverityLevels() {
        for (const cat of this.categories)
            cat.checkEventSeverityLevels();
    }

    handleOnlineClick($evt: any) {
        // The checkbox two-way-binds onlineOnly and triggers the filter; no extra work here.
    }
}
