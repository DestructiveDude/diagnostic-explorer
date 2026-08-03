import {SubCat} from './SubCat';
import {Subject} from 'rxjs';
import {getErrorMessage, strEqCI} from '../util/util';
import {OperationSet} from './DiagResponse';
import {RealtimeModel} from './RealtimeModel';
import {OperationModel} from './OperationModel';
import {ExecOperationRequest} from './ExecOperationRequest';
import {OperationResponse} from './SetPropertyRequest';
import {Null} from '../util/Null';
import {Clipboard} from '@angular/cdk/clipboard';

export class ExecOperationsModel {
    finished = new Subject<void>();
    readonly operations: OperationModel[] = [];
    activeOperation?: OperationModel;
    results = '';
    executing = false;
    executeDate: Null<Date> = null;

    constructor(readonly realtimeModel: RealtimeModel,
                readonly subCat: SubCat,
                private readonly clipboard: Clipboard) {

        const opSet: OperationSet | undefined = this.realtimeModel.operationSets.find(os => strEqCI(os.id, this.subCat.operationSet));

        if (opSet)
            this.operations = opSet.operations.map(op => new OperationModel(op));
    }

    closeClick() {
        this.finished.next();
        this.finished.complete();
    }

    selectOperation(op: OperationModel) {
        this.activeOperation = op;
    }

    handleMouseOver(evt: MouseEvent, op: OperationModel) {
        if (evt.buttons === 1)
            this.selectOperation(op);
    }

    async execute(): Promise<void> {
        // Guard the non-null derefs: clicking Execute before selecting an operation (or with no
        // active process) previously threw a TypeError that was caught below and shown as the
        // operation "result", hiding the real cause. The button is also disabled in this state.
        const process = this.realtimeModel.activeProcess;
        const operation = this.activeOperation;
        if (!process || !operation) {
            this.results = 'Select a process and an operation before executing.';
            return;
        }

        try {
            this.executing = true;
            this.results = '';
            this.executeDate = null;

            const request = new ExecOperationRequest();
            request.id = process.id;
            request.path = this.subCat.cat.name + '|' + this.subCat.name;
            request.operation = operation.signature;
            request.arguments = operation.parameters.map(p => p.value);

            const result: OperationResponse = await this.realtimeModel.hubService.executeOperation(request);

            if (result.isSuccess)
                this.results = result.result ?? 'Success';
            else
                this.results = result.errorMessage;
        } catch (err) {
            console.log(err);
            this.results = getErrorMessage(err);
        } finally {
            this.executing = false;
            this.executeDate = new Date();
        }
    }

    copyToClipboard() {
        this.clipboard.copy(this.results);

        this.realtimeModel.messages.add({ severity: 'success', detail: 'Result copied to clipboard', life: 2000 });
    }
}

