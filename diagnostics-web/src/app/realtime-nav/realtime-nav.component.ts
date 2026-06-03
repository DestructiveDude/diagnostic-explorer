import {Component} from '@angular/core';
import {AppModel} from '../Model/AppModel';
import {DiagProcess} from '../Model/DiagProcess';
import {RealtimeModel} from '../Model/RealtimeModel';
import {MenuItem} from 'primeng/api';

@Component({
    selector: 'app-realtime-nav',
    templateUrl: './realtime-nav.component.html',
    styleUrls: ['./realtime-nav.component.scss'],
    standalone: false
})
export class RealtimeNavComponent {

    // Row the PrimeNG context menu currently targets (set via pContextMenuRow).
    selectedProcess?: DiagProcess;

    readonly contextMenuItems: MenuItem[] = [
        {label: 'Retro', command: () => this.selectedProcess && this.app.showRetro(this.selectedProcess)},
        {label: 'Delete', command: () => this.selectedProcess && this.model.deleteProcess(this.selectedProcess)},
    ];

    constructor(readonly app: AppModel, readonly model: RealtimeModel) {
    }

    getProcess(item: any): DiagProcess {
        return item as DiagProcess;
    }
}
