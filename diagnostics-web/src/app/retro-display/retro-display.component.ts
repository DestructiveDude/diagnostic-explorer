import {Component, OnInit} from '@angular/core';
import {RetroModel} from '../Model/RetroModel';
import {EventModel} from '../Model/EventModel';
import {SystemEvent} from '../Model/DiagResponse';

@Component({
    selector: 'app-retro-display',
    templateUrl: './retro-display.component.html',
    styleUrls: ['./retro-display.component.scss'],
    standalone: false
})
export class RetroDisplayComponent implements OnInit {

    constructor(readonly model: RetroModel) {
    }

    ngOnInit(): void {
    }

    /**
     * Adapts the selected DiagnosticMsg (retro model) to an EventModel so it can
     * be passed to app-event-detail, which expects EventModel.
     * DiagnosticMsg extends SystemEvent, so all required fields are present.
     */
    get selectedAsEventModel(): EventModel | undefined {
        const src = this.model.selectedEvent;
        if (!src) return undefined;
        const evt = new EventModel(src as unknown as SystemEvent);
        evt.machine = src.machine;
        evt.process = src.process;
        evt.user = src.user;
        evt.isSelected = src.isSelected;
        return evt;
    }

}
