import {ChangeDetectionStrategy, Component, Input, OnChanges} from '@angular/core';
import {DrillDownEventViewDefinition} from '../Model/DrillDownRequest';
import {LogStreamEvent, routeMatches, toSystemEvent} from '../Model/LogStream';
import {EventModel} from '../Model/EventModel';
import {FilterCriteria} from '../Model/FilterCriteria';

@Component({
    selector: 'app-event-sink-view',
    standalone: false,
    templateUrl: './event-sink-view.component.html',
    styles: [':host { display: block; margin: 1rem 0; } .events { max-height: 260px; overflow: auto; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class EventSinkViewComponent implements OnChanges {
    @Input() view = new DrillDownEventViewDefinition();
    @Input() events: LogStreamEvent[] = [];
    rows: {event: LogStreamEvent, model: EventModel}[] = [];
    filteredRows: typeof this.rows = [];
    selected?: LogStreamEvent;
    criteria = new FilterCriteria();
    filterVisible = false;

    ngOnChanges(): void {
        this.rows = this.events.filter(event => this.view.matchers.some(matcher => routeMatches(matcher, event)))
            .map(event => ({event, model: new EventModel(toSystemEvent(event))}));
        this.filter(this.criteria);
    }

    filter(criteria: FilterCriteria): void {
        this.criteria = criteria;
        this.filteredRows = this.rows.filter(row => criteria.filter(row.model));
        this.selected = this.filteredRows.find(row => row.event.streamId === this.selected?.streamId
            && row.event.sequence === this.selected?.sequence)?.event;
    }
}
