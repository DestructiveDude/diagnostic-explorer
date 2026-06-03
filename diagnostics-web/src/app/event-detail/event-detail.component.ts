import { Component, EventEmitter, Input, Output } from '@angular/core';
import { EventModel } from '../Model/EventModel';

@Component({
  selector: 'app-event-detail',
  templateUrl: './event-detail.component.html',
  styleUrls: ['./event-detail.component.scss'],
  standalone: false,
})
export class EventDetailComponent {
  @Input() event?: EventModel;
  @Output() closed = new EventEmitter<void>();
}
