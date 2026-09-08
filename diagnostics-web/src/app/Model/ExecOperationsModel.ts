import {Subject} from 'rxjs';
import {getErrorMessage, strEqCI} from '../util/util';
import {OperationSet} from './DiagResponse';
import {RealtimeModel} from './RealtimeModel';
import {OperationModel} from './OperationModel';
import {ExecOperationRequest} from './ExecOperationRequest';
import {OperationResponse} from './SetPropertyRequest';
import {Null} from '../util/Null';
import {Clipboard} from '@angular/cdk/clipboard';
import {DrillDownRequest} from './DrillDownRequest';

export class ExecOperationsModel {
    finished = new Subject<void>();
    completed = new Subject<void>();
    readonly operations: OperationModel[] = [];
    activeOperation?: OperationModel;
    results = '';
    executing = false;
    executeDate: Null<Date> = null;

    constructor(readonly realtimeModel: RealtimeModel,
                readonly target: {operationSet: string; getPropertyPath(): string},
                private readonly clipboard: Clipboard,
                private readonly context?: Pick<DrillDownRequest, 'id' | 'objectPaths'>,
                operationSets = realtimeModel.operationSets) {

        const opSet: OperationSet | undefined = operationSets.find(os => strEqCI(os.id, this.target.operationSet));

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
        const processId = this.context?.id ?? this.realtimeModel.activeProcess?.id;
        const operation = this.activeOperation;
        if (!processId || !operation) {
            this.results = 'Select a process and an operation before executing.';
            return;
        }

        try {
            this.executing = true;
            this.results = '';
            this.executeDate = null;

            const request = new ExecOperationRequest();
            request.id = processId;
            request.objectPaths = [...this.context?.objectPaths ?? []];
            request.path = this.target.getPropertyPath();
            request.operation = operation.signature;
            request.arguments = operation.parameters.map(p => p.value);

            const result: OperationResponse = await this.realtimeModel.hubService.executeOperation(request);

            if (result.isSuccess) {
                this.results = result.result ?? 'Success';
            } else
                this.results = result.errorMessage;
        } catch (err) {
            console.log(err);
            this.results = getErrorMessage(err);
        } finally {
            this.executing = false;
            this.executeDate = new Date();
            this.completed.next();
        }
    }

    copyToClipboard() {
        this.clipboard.copy(this.results);

        this.realtimeModel.messages.add({ severity: 'success', detail: 'Result copied to clipboard', life: 2000 });
    }
}

