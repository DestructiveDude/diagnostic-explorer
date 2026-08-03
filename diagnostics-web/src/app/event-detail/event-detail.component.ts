import { Component, EventEmitter, Input, Output, ChangeDetectionStrategy } from '@angular/core';
import { EventModel } from '../Model/EventModel';

@Component({
  selector: 'app-event-detail',
  templateUrl: './event-detail.component.html',
  styleUrls: ['./event-detail.component.scss'],
  changeDetection: ChangeDetectionStrategy.Eager,
  standalone: false,
})
export class EventDetailComponent {
  @Input() event?: EventModel;
  @Output() closed = new EventEmitter<void>();
}
