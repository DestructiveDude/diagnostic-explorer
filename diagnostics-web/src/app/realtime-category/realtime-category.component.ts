import {Component, Input, OnInit} from '@angular/core';
import {CategoryModel} from '../Model/CategoryModel';
import {MatSnackBar} from '@angular/material/snack-bar';
import {MatSnackBarConfig} from '@angular/material/snack-bar';
import {Clipboard} from '@angular/cdk/clipboard';
import {PropModel} from '../Model/PropModel';
import {SetPropertyDialogComponent} from '../set-property-dialog/set-property-dialog.component';
import {MatDialog} from '@angular/material/dialog';
import {PromptData, PromptResult} from '../util/PromptResult';
import {SubCat} from '../Model/SubCat';
import {ExecOperationsModel} from '../Model/ExecOperationsModel';
import {ExecOperationsComponent} from '../exec-operations/exec-operations.component';
import {RealtimeModel} from '../Model/RealtimeModel';

@Component({
    selector: 'app-realtime-category',
    templateUrl: './realtime-category.component.html',
    styleUrls: ['./realtime-category.component.scss'],
    standalone: false
})
export class RealtimeCategoryComponent implements OnInit {

    @Input()
    category?: CategoryModel;

    constructor(private _snackBar: MatSnackBar, private realtimeModel: RealtimeModel, private dialog: MatDialog) {
    }

    ngOnInit(): void {
    }

    handleDoubleClick(prop: PropModel, evt: MouseEvent) {
        if (evt.detail === 2) {
            new Clipboard(document).copy(prop.value);

            const config: MatSnackBarConfig = {
                horizontalPosition: 'center',
                verticalPosition: 'top',
                politeness: 'assertive',
                panelClass: 'value-copied-snackbar',
                duration: 1000,
            };
            this._snackBar.open('Value copied to clipboard!', '', config);
        }
    }

    handleClick($event: MouseEvent) {
        $event.stopPropagation();
    }

    showOperationsDialog(evt: MouseEvent, subCat: SubCat): void {

        evt.cancelBubble = true;
        const model = new ExecOperationsModel(this.realtimeModel, subCat);

        const dialogRef = this.dialog.open(ExecOperationsComponent, {
            disableClose: true,
            panelClass: 'operations-dialog-panel',
            width: '600px',
            // height: '450px',
            data: model
        });

        model.finished.subscribe(_ => dialogRef.close());
    }

    showSetPropertyDialog(prop: PropModel): void {
        // Label the field with the human-friendly property name, not the internal pipe-delimited
        // path (which also exposes an empty PropCategory segment, e.g. "Trading|OrderEngine||MaxOrders").
        // The full path is still used for the write itself via setPropertyValue(prop, ...).
        const data = new PromptData(prop.name, prop.value);

        const dialogRef = this.dialog.open(SetPropertyDialogComponent, {
            disableClose: true,
            width: '500px',
            height: '250px',
            data: data,
        });

        dialogRef.afterClosed().subscribe(async (result: PromptResult) => {
            if (result.button === 'OK')
                await this.category!.realtimeModel.setPropertyValue(prop, result.value);
        });
    }
}
