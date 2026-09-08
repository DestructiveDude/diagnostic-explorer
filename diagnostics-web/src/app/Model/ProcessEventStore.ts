import {LogStreamEvent, LogStreamInitialization, LogStreamRoutingConfiguration} from './LogStream';

const defaultMaxEvents = 5_000;
const defaultMaxAgeMinutes = 5;

/** The retained, authoritative log-stream snapshot for one process. */
export class ProcessEventStore {
    events: LogStreamEvent[] = [];
    routing?: LogStreamRoutingConfiguration;
    streamId?: string;
    private maxEvents = defaultMaxEvents;
    private maxAgeMilliseconds = defaultMaxAgeMinutes * 60_000;

    constructor(private readonly readNow: () => number = () => Date.now()) {
    }

    initialize(initialization: LogStreamInitialization): void {
        this.streamId = initialization.streamId;
        this.routing = initialization.routing;
        this.maxEvents = positiveFinite(initialization.maxEvents, defaultMaxEvents);
        this.maxAgeMilliseconds = positiveFinite(initialization.maxAgeMinutes, defaultMaxAgeMinutes) * 60_000;
        this.events = [];
        this.append(initialization.replayEvents ?? []);
    }

    append(events: LogStreamEvent[]): void {
        if (!this.streamId) return;

        const retained = new Map(this.events.map(event => [event.sequence, event]));
        for (const event of events) {
            if (event.streamId === this.streamId)
                retained.set(event.sequence, event);
        }
        this.events = [...retained.values()].sort((left, right) => right.sequence - left.sequence);
        this.prune();
    }

    prune(): void {
        const oldest = this.readNow() - this.maxAgeMilliseconds;
        this.events = this.events
            .filter(event => Date.parse(event.timestampUtc) >= oldest)
            .slice(0, this.maxEvents);
    }
}

function positiveFinite(value: number, fallback: number): number {
    return Number.isFinite(value) && value > 0 ? value : fallback;
}
