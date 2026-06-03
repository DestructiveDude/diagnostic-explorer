import {Injectable} from '@angular/core';
import {RealtimeModel} from './RealtimeModel';
import {RetroModel} from './RetroModel';
import * as _ from 'lodash';
import {DiagHubService} from '../services/diag-hub.service';
import {DiagProcess} from './DiagProcess';

@Injectable()
export class AppModel {
    tabIndex = 0;
    private watchEnabled = false;

    lots: number[] = Array.from(new Array(200).keys());

    constructor(readonly realtimeModel: RealtimeModel,
                readonly hubService: DiagHubService,
                readonly retroModel: RetroModel) {
        this.watchEnabled = true;
    }


    viewRealtime() {
        this.tabIndex = 0;
    }

    viewRetro() {
        this.tabIndex = 1;
    }

    async start(): Promise<void> {
        await this.hubService.connect();
        await this.realtimeModel.start();
        // .then(async () => console.log(await connection.invoke("register", "Hello")));
    }

    get titleMessage(): string {
        return this.tabIndex === 0
            ? this.realtimeModel.titleMessage
            : this.retroModel.titleMessage;
    }

    get mainMessage(): string {
        return this.tabIndex === 0
            ? this.realtimeModel.mainMessage
            : this.retroModel.mainMessage;
    }

    get mainMessageClass(): string {
        return this.tabIndex === 0
            ? this.realtimeModel.mainMessageClass
            : this.retroModel.mainMessageClass;
    }

    get mainMessageCanClick(): boolean {
        return this.mainMessageClick !== _.noop;
    }

    get mainMessageClick(): (() => void) {
        return this.tabIndex === 0
            ? this.realtimeModel.mainMessageClick
            : this.retroModel.mainMessageClick;
    }

    async showRetro(item: DiagProcess): Promise<void> {
        this.tabIndex = 1;
        await this.retroModel.searchProcess(item);
    }
}

