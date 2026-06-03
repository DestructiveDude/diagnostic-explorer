import {Component, Input, OnInit} from '@angular/core';
import {EventSinkModel} from '../Model/EventSinkModel';
import {RealtimeModel} from '../Model/RealtimeModel';

@Component({
    selector: 'app-realtime-events',
    templateUrl: './realtime-events.component.html',
    styleUrls: ['./realtime-events.component.scss'],
    standalone: false
})
export class RealtimeEventsComponent implements OnInit {

    @Input() sink?: EventSinkModel;

    constructor(readonly realtime: RealtimeModel) {
    }

    ngOnInit(): void {
    }

}
