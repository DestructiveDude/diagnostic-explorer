import {DiagProcess} from './DiagProcess';
import {Subscription, timer} from 'rxjs';
import {Null} from '../util/Null';
import {Watch} from '../util/Watch';
import {DiagnosticResponse, OperationSet, PropertyBag, SystemEvent} from './DiagResponse';
import {destinationKey, LogStreamEvent, LogStreamInitialization, resolveDestinations, toSystemEvent} from './LogStream';
import _ from 'lodash';
import {escapeRegExp} from 'lodash';
import {customMerge, simpleMerge} from '../util/Merge';
import {Injectable} from '@angular/core';
import {CategoryModel} from './CategoryModel';
import {EventModel} from './EventModel';
import {PropModel} from './PropModel';
import {SetPropertyRequest} from './SetPropertyRequest';
import {DialogService} from 'primeng/dynamicdialog';
import {InfoDialogComponent} from '../info-dialog/info-dialog.component';
import {MessageService} from 'primeng/api';
import {plainToInstance} from 'class-transformer';
import {DiagHubService} from '../services/diag-hub.service';
import {DatePipe} from '@angular/common';
import {strEqCI} from '../util/util';
import {DrillDownRequest} from './DrillDownRequest';
import {ProcessEventStore} from './ProcessEventStore';

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

    private readonly processEventStores = new Map<string, ProcessEventStore>();
    private readonly eventModels = new Map<string, EventModel>();

    get logStreamEvents(): LogStreamEvent[] {
        return this.activeProcess ? this.processEventStores.get(this.activeProcess.id)?.events ?? [] : [];
    }

    @Watch((_this: RealtimeModel) => _this.performProcessSearch())
    processSearch: Null<string> = null;
    watchEnabled = false;

    @Watch((_this: RealtimeModel) => _this.performProcessSearch())
    onlineOnly = true;
    activeCat?: CategoryModel;
    selectedIndex = 0;

    constructor(readonly hubService: DiagHubService,
                readonly datePipe: DatePipe,
                private dialog: DialogService,
                readonly messages: MessageService) {
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
                    this.messages.add({ severity: 'error', detail: message, life: 2000 });
            });
            connection.on('InitializeLogStream', (id: string, initialization: LogStreamInitialization) => {
                if (id === this.activeProcess?.id)
                    this.initializeLogStream(id, initialization);
            });
            connection.on('StreamLogEvents', (id: string, events: LogStreamEvent[]) => {
                if (id === this.activeProcess?.id)
                    this.streamLogEvents(id, events);
            });
        });

        this.hubService.connectionStarted.subscribe(_connection => {
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
            .subscribe(_folder => this.checkEventSeverityLevels());

    }

    async selectProcess(process: DiagProcess) {
        if (this.selectedEvent) {
            this.selectedEvent.isSelected = false;
        }
        this.activeProcess = process;
        this.categories = [];
        this.operationSets = [];
        this.selectedEvent = undefined;
        this.activeCat = undefined;
        this.selectedIndex = 0;
        this.traceScopeVisible = false;

        this.reconcileEventProjections();

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


        cats = cats.filter(c => c.subCats.length || c.eventSinks.length);

        this.categories = cats;

        this.reconcileActiveCategory();

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
        this.mergeProcesses(processes, true);
    }

    public updateProcess(process: DiagProcess): void {
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

        if (removeOthers) {
            const ids = new Set(processes.map(process => process.id));
            for (const id of this.processEventStores.keys()) {
                if (!ids.has(id)) this.removeProcessEventStore(id);
            }
        }

        if (this.activeProcess && !this.allProcesses.some(p => p.id === this.activeProcess!.id)) {
            this.removeProcess(this.activeProcess.id);
        }
    }

    public removeProcess(id: string) {
        this.allProcesses = this.allProcesses.filter(p => p.id !== id);
        this.filteredProcesses = this.filteredProcesses.filter(p => p.id !== id);
        this.removeProcessEventStore(id);

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

    async setPropertyValue(prop: PropModel, value: string,
                           context?: Pick<DrillDownRequest, 'id' | 'objectPaths'>): Promise<boolean> {
        try {
            const request = new SetPropertyRequest();
            request.id = context?.id ?? this.activeProcess!.id;
            request.objectPaths = [...context?.objectPaths ?? []];
            request.path = prop.getPropertyPath();
            request.value = value;

            const result = await this.hubService.setPropertyValue(request);
            if (!result.isSuccess) {
                console.log(result);
                this.showError('Error setting property', result.errorMessage || 'Property was not set');
            } else {
                this.messages.add({ severity: 'success', detail: 'Property set!', life: 1000 });
                return true;
            }
        } catch (err: any) {
            console.log(err);
            this.showError('Error setting property', 'See console for details');
        }
        return false;
    }

    private showError(title: string, message: string) {
        this.dialog.open(InfoDialogComponent, {
            header: title,
            width: '400px',
            modal: true,
            closable: true,
            data: { title, message },
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


    /**
     * Replaces this process's events with the stream snapshot.
     *
     * An initialization is a whole picture, not an increment: it arrives when a browser attaches
     * and again whenever an agent reconnects, and in the second case it may carry a different
     * stream whose sequence numbers mean something else. Clearing first is what stops the two
     * being interleaved. The routing it carries is kept, because every event that follows is
     * placed with it.
     */
    private initializeLogStream(id: string, initialization: LogStreamInitialization): void {
        if (this.activeProcess?.id !== id) return;

        const store = this.getProcessEventStore(id);
        store.initialize(initialization);
        this.reconcileEventProjections();
        this.recordEventSeverity(store.events, store);
    }

    private streamLogEvents(id: string, events: LogStreamEvent[]): void {
        if (this.activeProcess?.id !== id) return;

        const store = this.getProcessEventStore(id);
        const known = new Set(store.events.map(event => this.eventModelKey(id, event)));
        store.append(events);
        this.reconcileEventProjections();
        this.recordEventSeverity(events.filter(event => event.streamId === store.streamId
            && !known.has(this.eventModelKey(id, event)) && store.events.includes(event)), store);
    }

    getProcessEventStore(id: string): ProcessEventStore {
        let store = this.processEventStores.get(id);
        if (!store) {
            store = new ProcessEventStore();
            this.processEventStores.set(id, store);
        }
        return store;
    }

    private reconcileEventProjections(): void {
        const process = this.activeProcess;
        const store = process && this.processEventStores.get(process.id);
        if (!process || !store) return;

        const categories = new Map<string, {name: string, sinks: Map<string, {name: string, events: EventModel[]}>}>();
        const addDestination = (category: string, name: string, event?: LogStreamEvent) => {
            const categoryKey = destinationKey(category, '');
            let projection = categories.get(categoryKey);
            if (!projection) {
                projection = {name: category, sinks: new Map()};
                categories.set(categoryKey, projection);
            }
            const key = destinationKey(category, name);
            let sink = projection.sinks.get(key);
            if (!sink) {
                sink = {name, events: []};
                projection.sinks.set(key, sink);
            }
            if (event) sink.events.push(this.getEventModel(process.id, event));
        };

        for (const route of store.routing?.routes ?? []) {
            for (const destination of route.destinations ?? []) {
                if (destination.category.source === 'Fixed' && destination.name.source === 'Fixed'
                    && destination.category.value && destination.name.value)
                    addDestination(destination.category.value, destination.name.value);
            }
        }
        for (const event of store.events) {
            for (const destination of resolveDestinations(store.routing, event))
                addDestination(destination.category, destination.name, event);
        }

        for (const projection of categories.values()) this.getCat(projection.name);
        for (const category of this.categories)
            category.reconcileEventSinks([...categories.get(destinationKey(category.name, ''))?.sinks.values() ?? []]);
        this.categories = this.categories.filter(category => category.subCats.length || category.eventSinks.length);
        this.reconcileActiveCategory();
        this.removeStaleEventModels(process.id, store.events);
        this.clearStaleSelectedEvent();
    }

    private getEventModel(processId: string, event: LogStreamEvent): EventModel {
        const key = this.eventModelKey(processId, event);
        const systemEvent = toSystemEvent(event);
        const existing = this.eventModels.get(key);
        if (existing && existing.id === systemEvent.id && existing.date === systemEvent.date
            && existing.level === systemEvent.level && existing.message === systemEvent.message
            && existing.detail === systemEvent.detail) return existing;

        const converted = new EventModel(systemEvent);
        if (existing) {
            const selected = existing.isSelected;
            Object.assign(existing, converted, {isSelected: selected});
            return existing;
        }
        this.eventModels.set(key, converted);
        return converted;
    }

    private removeStaleEventModels(processId: string, events: LogStreamEvent[]): void {
        const retained = new Set(events.map(event => this.eventModelKey(processId, event)));
        for (const key of this.eventModels.keys()) {
            if (key.startsWith(`${processId}\u001f`) && !retained.has(key)) this.eventModels.delete(key);
        }
    }

    private removeProcessEventStore(id: string): void {
        this.processEventStores.delete(id);
        for (const key of this.eventModels.keys()) {
            if (key.startsWith(`${id}\u001f`)) this.eventModels.delete(key);
        }
    }

    private eventModelKey(processId: string, event: LogStreamEvent): string {
        return `${processId}\u001f${event.streamId}\u001f${event.sequence}`;
    }

    private clearStaleSelectedEvent(): void {
        if (this.selectedEvent && !this.categories.some(category =>
            category.eventSinks.some(sink => sink.events.includes(this.selectedEvent!)))) {
            this.selectedEvent.isSelected = false;
            this.selectedEvent = undefined;
            this.traceScopeVisible = false;
        }
    }

    private recordEventSeverity(events: LogStreamEvent[], store: ProcessEventStore): void {
        const grouped = _.groupBy<SystemEvent>(events.flatMap(event =>
            resolveDestinations(store.routing, event).map(destination =>
                toSystemEvent(event, destination.category, destination.name))), event => event.sinkCategory);
        for (const category in grouped)
            this.getCat(category).recordEventSeverity(grouped[category]);
    }

    private reconcileActiveCategory(): void {
        if (this.activeCat) {
            const foundIndex = this.categories.findIndex(category => category.name === this.activeCat!.name);
            if (foundIndex >= 0) {
                this.activeCat = this.categories[foundIndex];
                this.selectedIndex = foundIndex;
            } else if (this.categories.length > 0) {
                this.selectedIndex = Math.max(0, Math.min(this.selectedIndex, this.categories.length - 1));
                this.activeCat = this.categories[this.selectedIndex];
            } else {
                this.selectedIndex = 0;
                this.activeCat = undefined;
            }
        } else if (this.categories.length > 0) {
            this.selectedIndex = Math.max(0, Math.min(this.selectedIndex, this.categories.length - 1));
            this.activeCat = this.categories[this.selectedIndex];
        } else {
            this.selectedIndex = 0;
            this.activeCat = undefined;
        }
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
        for (const [id, store] of this.processEventStores) {
            store.prune();
            this.removeStaleEventModels(id, store.events);
        }
        this.reconcileEventProjections();
        for (const cat of this.categories)
            cat.checkEventSeverityLevels();
    }

    handleOnlineClick(_$evt: any) {
        // The checkbox two-way-binds onlineOnly and triggers the filter; no extra work here.
    }
}
