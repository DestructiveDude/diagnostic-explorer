import {LogStreamEvent, LogStreamInitialization} from './LogStream';
import {ProcessEventStore} from './ProcessEventStore';

let now = Date.parse('2026-09-08T10:00:00.000Z');

function event(sequence: number, over: Partial<LogStreamEvent> = {}): LogStreamEvent {
    return {
        streamId: 'stream-1',
        sequence,
        timestampUtc: new Date(now).toISOString(),
        loggerCategory: 'App.Worker',
        level: 2,
        eventId: 0,
        ...over,
    };
}

function initialization(over: Partial<LogStreamInitialization> = {}): LogStreamInitialization {
    return {
        streamId: 'stream-1',
        routing: {matchMode: 'AllMatches', routes: []},
        replayEvents: [],
        highWatermark: 0,
        maxEvents: 2,
        maxAgeMinutes: 5,
        ...over,
    };
}

describe('ProcessEventStore', () => {
    beforeEach(() => now = Date.parse('2026-09-08T10:00:00.000Z'));

    it('deduplicates out-of-order records, retains the newest sequences, and rejects another stream', () => {
        const store = new ProcessEventStore(() => now);
        store.initialize(initialization());

        store.append([event(3), event(1), event(2), event(3), event(4, {streamId: 'old'})]);

        expect(store.events.map(item => item.sequence)).toEqual([3, 2]);
    });

    it('expires records without new traffic', () => {
        const store = new ProcessEventStore(() => now);
        store.initialize(initialization({maxEvents: 10, maxAgeMinutes: 1, replayEvents: [event(1)]}));

        now += 60_001;
        store.prune();

        expect(store.events).toEqual([]);
    });

    it('treats every initialization as an authoritative replacement, including the same stream', () => {
        const store = new ProcessEventStore(() => now);
        store.initialize(initialization({replayEvents: [event(1)]}));
        store.append([event(2)]);

        store.initialize(initialization({replayEvents: [event(9)]}));
        expect(store.events.map(item => item.sequence)).toEqual([9]);

        store.initialize(initialization({streamId: 'stream-2', replayEvents: [event(1, {streamId: 'stream-2'})]}));
        expect(store.events.map(item => item.streamId)).toEqual(['stream-2']);
    });
});
