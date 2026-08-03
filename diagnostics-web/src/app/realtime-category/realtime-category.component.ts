import {Component, Input, OnInit, ChangeDetectionStrategy} from '@angular/core';
import {CategoryModel} from '../Model/CategoryModel';
import {MessageService} from 'primeng/api';
import {Clipboard} from '@angular/cdk/clipboard';
import {PropModel} from '../Model/PropModel';
import {SetPropertyDialogComponent} from '../set-property-dialog/set-property-dialog.component';
import {DialogService} from 'primeng/dynamicdialog';
import {PromptData, PromptResult} from '../util/PromptResult';
import {SubCat} from '../Model/SubCat';
import {ExecOperationsModel} from '../Model/ExecOperationsModel';
import {ExecOperationsComponent} from '../exec-operations/exec-operations.component';
import {RealtimeModel} from '../Model/RealtimeModel';

@Component({
    selector: 'app-realtime-category',
    templateUrl: './realtime-category.component.html',
    styleUrls: ['./realtime-category.component.scss'],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class RealtimeCategoryComponent implements OnInit {

    @Input()
    category?: CategoryModel;

    constructor(private messages: MessageService, private realtimeModel: RealtimeModel, private dialogService: DialogService,
                private clipboard: Clipboard) {
    }

    ngOnInit(): void {
    }

    handleDoubleClick(prop: PropModel, evt: MouseEvent) {
        if (evt.detail === 2) {
            this.clipboard.copy(prop.value);
            this.messages.add({ severity: 'success', detail: 'Value copied to clipboard!', life: 1000 });
        }
    }

    handleClick($event: MouseEvent) {
        $event.stopPropagation();
    }

    showOperationsDialog(evt: MouseEvent, subCat: SubCat): void {

        evt.cancelBubble = true;
        const model = new ExecOperationsModel(this.realtimeModel, subCat, this.clipboard);

        const ref = this.dialogService.open(ExecOperationsComponent, {
            header: 'Execute Operation',
            width: '600px',
            modal: true,
            closable: true,
            data: model,
        });

        model.finished.subscribe(_ => ref?.close());
    }

    showSetPropertyDialog(prop: PropModel): void {
        // Label the field with the human-friendly property name, not the internal pipe-delimited
        // path (which also exposes an empty PropCategory segment, e.g. "Trading|OrderEngine||MaxOrders").
        // The full path is still used for the write itself via setPropertyValue(prop, ...).
        const data = new PromptData(prop.name, prop.value);

        const ref = this.dialogService.open(SetPropertyDialogComponent, {
            header: 'Set Property',
            width: '500px',
            modal: true,
            closable: true,
            data,
        });

        ref?.onClose.subscribe(async (result: PromptResult) => {
            if (result?.button === 'OK')
                await this.category!.realtimeModel.setPropertyValue(prop, result.value);
        });
    }
}
