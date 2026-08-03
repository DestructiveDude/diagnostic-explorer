import {Component, ChangeDetectionStrategy} from '@angular/core';
import {AppModel} from './Model/AppModel';
import {RealtimeModel} from './Model/RealtimeModel';
import {RetroModel} from './Model/RetroModel';

@Component({
    selector: 'app-root',
    templateUrl: './app.component.html',
    styleUrls: ['./app.component.scss'],
    providers: [AppModel, RealtimeModel, RetroModel],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class AppComponent {
    title = 'diagnostics-web';
    modes = [ { label: 'Realtime', value: 0 }, { label: 'Retro', value: 1 } ];

    constructor(readonly appModel: AppModel) {
        appModel.start();
    }
}
