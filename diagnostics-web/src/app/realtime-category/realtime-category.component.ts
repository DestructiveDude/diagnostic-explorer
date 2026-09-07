import {Component, Input, Output, EventEmitter, OnDestroy, ChangeDetectionStrategy} from '@angular/core';
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
import {DrillDownDialogData, DrillDownRequest} from '../Model/DrillDownRequest';
import {OperationSet} from '../Model/DiagResponse';
import {getErrorMessage} from '../util/util';

@Component({
    selector: 'app-realtime-category',
    templateUrl: './realtime-category.component.html',
    styleUrls: ['./realtime-category.component.scss'],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class RealtimeCategoryComponent implements OnDestroy {

    @Input()
    category?: CategoryModel;
    @Input() context?: DrillDownRequest;
    @Input() operationSets?: OperationSet[];
    @Output() inspect = new EventEmitter<DrillDownDialogData>();
    @Output() actionCompleted = new EventEmitter<void>();
    preview?: {prop: PropModel, json: boolean, text: string};

    constructor(private messages: MessageService, private realtimeModel: RealtimeModel, private dialogService: DialogService,
                private clipboard: Clipboard) {
    }

    ngOnDestroy(): void {
        this.preview = undefined;
    }

    private actionContext(): Pick<DrillDownRequest, 'id' | 'objectPaths'> {
        return {
            id: this.context?.id ?? this.realtimeModel.activeProcess?.id ?? '',
            objectPaths: [...this.context?.objectPaths ?? []]
        };
    }

    private drillDownRequest(path: string, jsonHover = false, excludeEventViews = false): DrillDownRequest {
        const context = this.actionContext();
        return {...new DrillDownRequest(), ...context,
            objectPaths: [...context.objectPaths, path], jsonHover, excludeEventViews};
    }

    openDrillDown(path: string, title: string, jsonHover = false): void {
        this.preview = undefined;
        this.inspect.emit({request: this.drillDownRequest(path, jsonHover), title});
    }

    async showPreview(prop: PropModel, json: boolean): Promise<void> {
        const preview = {prop, json, text: 'Loading preview…'};
        this.preview = preview;
        try {
            const response = await this.realtimeModel.hubService.getDrillDown(
                this.drillDownRequest(prop.getPropertyPath(), json, true));
            if (this.preview !== preview) return;
            const properties = response.diagnostics.propertyBags.map(bag => [bag.name.split('\u001f')[0],
                    ...bag.categories.flatMap(category => category.properties.map(p => `${p.name}: ${p.value}`))
                ].join('\n')).join('\n\n') || 'No properties';
            preview.text = response.errorMessage || response.diagnostics.exceptionMessage || response.json || properties;
            if (response.isTruncated)
                preview.text += `\nShowing ${response.displayedCount} of ${response.totalCount ?? 'unknown'} items (truncated).`;
        } catch (error) {
            if (this.preview === preview)
                preview.text = getErrorMessage(error) || 'Unable to load preview';
        }
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
        const model = new ExecOperationsModel(this.realtimeModel, subCat, this.clipboard,
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
        const subscription = model.completed.subscribe(() => {
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
                this.actionCompleted.emit();
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
