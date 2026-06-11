import {EventResponse, PropertyBag, SystemEvent} from './DiagResponse';
import {customMerge} from '../util/Merge';
import {EventSinkModel} from './EventSinkModel';
import {SubCat} from './SubCat';
import {RealtimeModel} from './RealtimeModel';
import * as _ from 'lodash';
import {Level} from './Level';
import {strEqCI} from '../util/util';

export class CategoryModel {
    name: string = '';
    propData: PropertyBag[] = [];
    eventData: EventResponse[] = [];
    subCats: SubCat[] = [];
    eventSinks: EventSinkModel[] = [];
    realtimeModel: RealtimeModel;
    labelClass = '';
    worstSev = 0;
    worstSevDate = new Date();


    constructor(realtimeModel: RealtimeModel, name: string, props: PropertyBag[] = []) {
        this.realtimeModel = realtimeModel;
        this.name = name;
        if (props)
            this.update(props);
    }

    update(props: PropertyBag[]) {
        this.propData = props;

        this.subCats = customMerge(props,
            this.subCats,
            s => s.name,
            t => t.name,
            s => new SubCat(this, s),
            (s, t) => t.update(s));
    }

    getSink(name: string): EventSinkModel {
        let sink = this.eventSinks.find(c => strEqCI(c.name, name));
        if (!sink)
            this.eventSinks.push(sink = new EventSinkModel(this, name));

        return sink;
    }

    addEvents(evts: SystemEvent[]) {
        const maxLevel = _.maxBy(evts, evt => evt.level)?.level ?? 0;

        if (maxLevel >= this.worstSev) {
            this.worstSev = maxLevel;
            this.worstSevDate = new Date();
            this.labelClass = this.worstSev === 0 ? '' : 'event-level-' + Level.LevelToString(this.worstSev).toLocaleLowerCase();
        } else if (maxLevel > 0) {
            this.worstSevDate = new Date();
        }

        const grouped = _.groupBy(evts, evt => evt.sinkName)
        for (const sinkName in grouped)
            this.getSink(sinkName).addEvents(grouped[sinkName]);
    }

    checkEventSeverityLevels() {
        if (this.worstSev > 0) {
            const time = new Date().valueOf() - this.worstSevDate.valueOf();

            if (time > 300_000) {
                this.worstSev = 0;
                this.labelClass = '';
            }
        }
    }
}
