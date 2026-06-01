import {SystemEvent} from './DiagResponse';
import {IFilterableEvent} from './IFilterableEvent';
import {ScopeNode} from "./ScopeNode";

export class EventModel extends SystemEvent implements IFilterableEvent {
    isSelected = false;
    machine = '';
    process = '';
    user = '';

    region?: ScopeNode;

    constructor(evt: SystemEvent) {
        super();

        this.id = evt.id;
        this.date = evt.date;
        this.level = evt.level;
        this.message = evt.message;
        this.detail = evt.detail;

        this.region = ScopeNode.parseTraceScope(this.displayText);
    }

    get displayText(): string {
        // `||` not `??`: detail deserializes to '' (the DiagResponse default) for a message-only
        // event, and '' ?? x keeps the empty string, rendering blank detail. Fall back on any falsy
        // detail so the message text is shown instead.
        return this.detail || this.message;
    }

}
