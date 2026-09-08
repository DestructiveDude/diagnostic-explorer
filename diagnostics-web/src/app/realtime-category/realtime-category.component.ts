import {Component, Input, Output, EventEmitter, ChangeDetectionStrategy} from '@angular/core';
import {CategoryModel} from '../Model/CategoryModel';
import {MessageService} from 'primeng/api';
import {Clipboard} from '@angular/cdk/clipboard';
import {PropModel} from '../Model/PropModel';
import {SetPropertyDialogComponent} from '../set-property-dialog/set-property-dialog.component';
import {DialogService} from 'primeng/dynamicdialog';
import {PromptData, PromptResult} from '../util/PromptResult';
import {ExecOperationsModel} from '../Model/ExecOperationsModel';
import {ExecOperationsComponent} from '../exec-operations/exec-operations.component';
import {RealtimeModel} from '../Model/RealtimeModel';
import {DrillDownDialogData, DrillDownRequest} from '../Model/DrillDownRequest';
import {OperationSet} from '../Model/DiagResponse';

@Component({
    selector: 'app-realtime-category',
    templateUrl: './realtime-category.component.html',
    styleUrls: ['./realtime-category.component.scss'],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class RealtimeCategoryComponent {

    @Input()
    category?: CategoryModel;
    @Input() context?: DrillDownRequest;
    @Input() operationSets?: OperationSet[];
    @Output() inspect = new EventEmitter<DrillDownDialogData>();
    @Output() actionCompleted = new EventEmitter<void>();

    constructor(private messages: MessageService, private realtimeModel: RealtimeModel, private dialogService: DialogService,
                private clipboard: Clipboard) {
    }

    private actionContext(): Pick<DrillDownRequest, 'id' | 'objectPaths'> {
        return {
            id: this.context?.id ?? this.realtimeModel.activeProcess?.id ?? '',
            objectPaths: [...this.context?.objectPaths ?? []]
        };
    }

    get previewProcessId(): string {
        return this.actionContext().id;
    }

    get previewObjectPaths(): string[] {
        return this.actionContext().objectPaths;
    }

    private drillDownRequest(path: string, jsonHover = false, excludeEventViews = false): DrillDownRequest {
        const context = this.actionContext();
        return {...new DrillDownRequest(), ...context,
            objectPaths: [...context.objectPaths, path], jsonHover, excludeEventViews};
    }

    openDrillDown(path: string, title: string, jsonHover = false): void {
        this.inspect.emit({request: this.drillDownRequest(path, jsonHover), title});
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

    showOperationsDialog(evt: MouseEvent, target: {operationSet: string; getPropertyPath(): string}): void {

        evt.cancelBubble = true;
        const model = new ExecOperationsModel(this.realtimeModel, target, this.clipboard,
            this.actionContext(), this.operationSets ?? this.realtimeModel.operationSets);

        const ref = this.dialogService.open(ExecOperationsComponent, {
            header: 'Execute Operation',
            width: '600px',
            modal: true,
            closable: true,
            data: model,
        });

        model.finished.subscribe(_ => ref?.close());
        let closed = false;
        let executed = false;
        const subscription = model.completed.subscribe(() => {
            executed = true;
            if (closed) {
                subscription.unsubscribe();
                this.actionCompleted.emit();
            }
        });
        ref?.onClose.subscribe(() => {
            closed = true;
            // A pending operation may still mutate the object after its dialog closes.
            if (!model.executing) {
                subscription.unsubscribe();
                if (executed) this.actionCompleted.emit();
            }
        });
    }

    showSetPropertyDialog(prop: PropModel): void {
        // Label the field with the human-friendly property name, not the internal pipe-delimited
        // path (which also exposes an empty PropCategory segment, e.g. "Trading|OrderEngine||MaxOrders").
        // The full path is still used for the write itself via setPropertyValue(prop, ...).
        const data = new PromptData(prop.name, prop.value);
        const context = this.actionContext();

        const ref = this.dialogService.open(SetPropertyDialogComponent, {
            header: 'Set Property',
            width: '500px',
            modal: true,
            closable: true,
            data,
        });

        ref?.onClose.subscribe(async (result: PromptResult) => {
            if (result?.button === 'OK' && await this.realtimeModel.setPropertyValue(prop, result.value, context))
                this.actionCompleted.emit();
        });
    }
}
