import {Component} from '@angular/core';
import {DynamicDialogRef, DynamicDialogConfig} from 'primeng/dynamicdialog';
import {PromptData, PromptResult} from '../util/PromptResult';

@Component({
    selector: 'app-set-property-dialog',
    templateUrl: './set-property-dialog.component.html',
    styleUrls: ['./set-property-dialog.component.scss'],
    standalone: false
})
export class SetPropertyDialogComponent {

    text = '';
    value = '';

    constructor(private dialogRef: DynamicDialogRef, config: DynamicDialogConfig) {
        const prompt = config.data as PromptData;
        this.text = prompt.text;
        this.value = prompt.value;
    }

    onCancelClick(): void {
        this.dialogRef.close(new PromptResult('Cancel', ''));
    }

    onOkClick(): void {
        this.dialogRef.close(new PromptResult('OK', this.value));
    }

    handleKeyUp(evt: KeyboardEvent) {
        // (removed console.log(evt.key) — it logged every keystroke of the value being set on a live process)
        if (evt.key === 'Enter')
            this.onOkClick();

        if (evt.key === 'Escape')
            this.onCancelClick();
    }
}
