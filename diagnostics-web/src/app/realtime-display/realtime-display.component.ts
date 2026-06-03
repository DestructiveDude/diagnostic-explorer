import {Component, OnInit} from '@angular/core';
import {RealtimeModel} from '../Model/RealtimeModel';

@Component({
    selector: 'app-realtime-display',
    templateUrl: './realtime-display.component.html',
    styleUrls: ['./realtime-display.component.scss'],
    standalone: false
})
export class RealtimeDisplayComponent implements OnInit {

    constructor(readonly model: RealtimeModel) {
    }

    ngOnInit(): void {
    }

    // p-tabs emits its value as string | number | undefined; the category tabs
    // use the numeric index, so coerce before driving the model.
    onCategoryTab(value: string | number | undefined): void {
        const index = Number(value);
        this.model.selectedIndex = index;
        this.model.handleSelectedTabChanged(index);
    }

}
