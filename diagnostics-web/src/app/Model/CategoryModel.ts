import {EventResponse, PropertyBag, SystemEvent} from './DiagResponse';
import {customMerge} from '../util/Merge';
import {EventSinkModel} from './EventSinkModel';
import {EventModel} from './EventModel';
import {SubCat} from './SubCat';
import {RealtimeModel} from './RealtimeModel';
import _ from 'lodash';
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

    reconcileEventSinks(projections: {name: string, events: EventModel[]}[]) {
        const sinks = projections.map(projection => {
            const sink = this.getSink(projection.name);
            sink.setEvents(projection.events);
            return sink;
        });
        this.eventSinks = sinks;

    }

    recordEventSeverity(events: SystemEvent[]) {
        const worstSev = _.maxBy(events, event => event.level)?.level ?? 0;
        if (worstSev > 0) {
            if (worstSev >= this.worstSev) this.worstSev = worstSev;
            this.worstSevDate = new Date();
            this.labelClass = 'event-level-' + Level.LevelToString(this.worstSev).toLocaleLowerCase();
        }
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
