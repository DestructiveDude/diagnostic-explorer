import {Component, OnInit, ChangeDetectionStrategy} from '@angular/core';
import {RealtimeModel} from '../Model/RealtimeModel';
import {DialogService} from 'primeng/dynamicdialog';
import {DrillDownDialogComponent} from '../drill-down-dialog/drill-down-dialog.component';
import {DrillDownDialogData} from '../Model/DrillDownRequest';

@Component({
    selector: 'app-realtime-display',
    templateUrl: './realtime-display.component.html',
    styleUrls: ['./realtime-display.component.scss'],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class RealtimeDisplayComponent implements OnInit {

    constructor(readonly model: RealtimeModel, private readonly dialogs: DialogService) {
    }

    ngOnInit(): void {
    }

    openDrillDown(data: DrillDownDialogData): void {
        this.dialogs.open(DrillDownDialogComponent, {
            header: 'Inspect ' + data.title,
            width: '1000px',
            style: {maxWidth: '95vw'},
            contentStyle: {maxHeight: '80vh', overflow: 'auto'},
            modal: true,
            closable: true,
            data: {...data, realtime: this.model}
        });
    }

    // p-tabs emits its value as string | number | undefined; the category tabs
    // use the numeric index, so coerce before driving the model.
    onCategoryTab(value: string | number | undefined): void {
        const index = Number(value);
        this.model.selectedIndex = index;
        this.model.handleSelectedTabChanged(index);
    }

}
