import {Component, ChangeDetectionStrategy} from '@angular/core';
import {ExecOperationsModel} from '../Model/ExecOperationsModel';
import {DynamicDialogConfig} from 'primeng/dynamicdialog';

@Component({
    selector: 'app-exec-operations',
    templateUrl: './exec-operations.component.html',
    styleUrls: ['./exec-operations.component.scss'],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class ExecOperationsComponent {

    readonly model: ExecOperationsModel;

    constructor(config: DynamicDialogConfig) {
        this.model = config.data as ExecOperationsModel;
    }
}
