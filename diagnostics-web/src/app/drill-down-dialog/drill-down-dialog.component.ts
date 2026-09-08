import {ChangeDetectionStrategy, Component, OnDestroy, OnInit} from '@angular/core';
import {DynamicDialogConfig} from 'primeng/dynamicdialog';
import {DrillDownDialogData, DrillDownRequest, DrillDownResponse} from '../Model/DrillDownRequest';
import {CategoryModel} from '../Model/CategoryModel';
import {RealtimeModel} from '../Model/RealtimeModel';
import {getErrorMessage} from '../util/util';
import _ from 'lodash';

@Component({
    selector: 'app-drill-down-dialog',
    standalone: false,
    templateUrl: './drill-down-dialog.component.html',
    styles: [':host { display: block; } pre { white-space: pre-wrap; overflow-wrap: anywhere; }'],
    changeDetection: ChangeDetectionStrategy.Eager,
    // Dynamic dialogs live outside AppComponent, where the shared realtime model is provided.
    providers: [{provide: RealtimeModel, useFactory: (config: DynamicDialogConfig) => config.data.realtime,
        deps: [DynamicDialogConfig]}]
})
export class DrillDownDialogComponent implements OnInit, OnDestroy {
    request: DrillDownRequest;
    breadcrumbs: DrillDownDialogData[];
    response?: DrillDownResponse;
    categories: CategoryModel[] = [];
    loading = false;
    private generation = 0;
    private releaseProcessEvents?: () => void;
    private retainedProcessId?: string;

    constructor(config: DynamicDialogConfig, readonly realtime: RealtimeModel) {
        const data = config.data as DrillDownDialogData;
        this.request = {...data.request, objectPaths: [...data.request.objectPaths]};
        this.breadcrumbs = [{title: data.title, request: this.request}];
    }

    ngOnInit(): void {
        this.refresh();
    }

    ngOnDestroy(): void {
        ++this.generation;
        this.releaseProcessEvents?.();
        this.releaseProcessEvents = undefined;
    }

    navigate(data: DrillDownDialogData): void {
        this.request = data.request;
        this.breadcrumbs = [...this.breadcrumbs, data];
        this.refresh();
    }

    goBack(index: number): void {
        this.breadcrumbs = this.breadcrumbs.slice(0, index + 1);
        this.request = this.breadcrumbs[index].request;
        this.refresh();
    }

    async refresh(): Promise<void> {
        ++this.generation;
        const generation = this.generation;
        this.loading = true;
        this.response = undefined;
        this.categories = [];
        try {
            const response = await this.realtime.hubService.getDrillDown(this.request);
            if (generation !== this.generation) return;
            this.response = response;
            this.updateEventRetention(response);
            const bags = _.groupBy(response.diagnostics.propertyBags, bag => bag.category);
            this.categories = Object.keys(bags).sort().map(name => new CategoryModel(this.realtime, name, bags[name]));
        } catch (error) {
            if (generation === this.generation)
                this.response = {...new DrillDownResponse(), errorMessage: getErrorMessage(error) || 'Unable to load diagnostics'};
        } finally {
            if (generation === this.generation) this.loading = false;
        }
    }

    private updateEventRetention(response: DrillDownResponse): void {
        const processId = response.eventViews.length ? this.request.id : undefined;
        if (processId === this.retainedProcessId) return;
        this.releaseProcessEvents?.();
        this.releaseProcessEvents = undefined;
        this.retainedProcessId = processId;
        if (processId) this.releaseProcessEvents = this.realtime.retainProcessEvents(processId);
    }
}
