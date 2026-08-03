import {Component, ChangeDetectionStrategy} from '@angular/core';
import {RetroModel} from '../Model/RetroModel';
import {EventModel} from '../Model/EventModel';
import {DiagnosticMsg} from '../Model/DiagnosticMsg';
import {ScopeNode} from '../Model/ScopeNode';

@Component({
    selector: 'app-retro-display',
    templateUrl: './retro-display.component.html',
    styleUrls: ['./retro-display.component.scss'],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class RetroDisplayComponent {

    resultsCollapsed = false;

    constructor(readonly model: RetroModel) {
    }

    // The shared app-event-detail panel takes an EventModel (which computes
    // displayText and a parsed trace-scope `region`); the retro model selects a
    // raw DiagnosticMsg. Adapt it here. Memoised by source reference: the getter
    // is re-read on every change-detection pass, and rebuilding the EventModel
    // each time would re-parse the trace scope and discard the per-node
    // `expanded` state, collapsing the tree on every tick.
    private cachedSrc?: DiagnosticMsg;
    private cachedEvent?: EventModel;

    private hasTraceScopeCache = new WeakMap<DiagnosticMsg, boolean>();

    // Only read by the template while model.selectedEvent is truthy (the @if
    // guards the app-event-detail binding), so there is no need to clear the
    // cache back to undefined.
    hasTraceScope(msg: DiagnosticMsg): boolean {
        let cached = this.hasTraceScopeCache.get(msg);
        if (cached === undefined) {
            cached = ScopeNode.hasTraceScope(msg.detail || msg.message);
            this.hasTraceScopeCache.set(msg, cached);
        }
        return cached;
    }

    get selectedAsEventModel(): EventModel | undefined {
        const src = this.model.selectedEvent;
        if (src && src !== this.cachedSrc) {
            const evt = new EventModel(src);
            evt.machine = src.machine;
            evt.process = src.process;
            evt.user = src.user;
            evt.isSelected = src.isSelected;
            this.cachedSrc = src;
            this.cachedEvent = evt;
        }
        return this.cachedEvent;
    }

}
