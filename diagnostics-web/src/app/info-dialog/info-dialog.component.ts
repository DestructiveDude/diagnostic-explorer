import {Component, ChangeDetectionStrategy} from '@angular/core';
import {DynamicDialogRef, DynamicDialogConfig} from 'primeng/dynamicdialog';
import {InfoDialogData} from '../Model/InfoDialogData';

@Component({
    selector: 'app-info-dialog',
    templateUrl: './info-dialog.component.html',
    styleUrls: ['./info-dialog.component.scss'],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class InfoDialogComponent {

    data: InfoDialogData;

    constructor(private dialogRef: DynamicDialogRef, config: DynamicDialogConfig) {
        this.data = config.data as InfoDialogData;
    }

    close(): void {
        this.dialogRef.close();
    }
}
