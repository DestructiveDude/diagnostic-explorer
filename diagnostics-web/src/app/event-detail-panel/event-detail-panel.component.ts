import {ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output} from '@angular/core';
import {LogStreamEvent, toSystemEvent} from '../Model/LogStream';
import {EventModel} from '../Model/EventModel';

@Component({
    selector: 'app-event-detail-panel',
    standalone: false,
    template: `
        @if (event) {
            <div class="text-xs p-2">{{ event.loggerCategory }} · Event {{ event.eventId }} {{ event.eventName }}</div>
            <app-event-detail [event]="model" (closed)="closed.emit()" />
        }
    `,
    styles: [':host { display: block; height: 300px; min-height: 0; margin-bottom: 2rem; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class EventDetailPanelComponent implements OnChanges {
    @Input() event?: LogStreamEvent;
    @Output() closed = new EventEmitter<void>();
    model?: EventModel;

    ngOnChanges(): void {
        this.model = this.event ? new EventModel(toSystemEvent(this.event)) : undefined;
    }
}
