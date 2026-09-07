import {
    LogStreamEvent,
    LogStreamRoute,
    LogStreamRouteValue,
    LogStreamRoutingConfiguration,
    resolveDestinations,
} from './LogStream';
import {Level} from './Level';

/**
 * These pin the client half of routing against the agent's EventSinkRouter. The two resolve the
 * same rules independently — the agent to decide whether to publish, the browser to decide where
 * to display — so a rule that drifts on one side silently misplaces events rather than failing.
 */

const fixed = (value: string): LogStreamRouteValue => ({source: 'Fixed', value});
const suffix: LogStreamRouteValue = {source: 'LoggerSuffix'};

function route(over: Partial<LogStreamRoute> = {}): LogStreamRoute {
    return {
        order: 0,
        loggerName: '*',
        loggerNameMatchMode: 'Wildcard',
        stopProcessing: false,
        destinations: [{category: fixed('Cat'), name: fixed('Sink')}],
        ...over,
    };
}

function routing(routes: LogStreamRoute[], matchMode: LogStreamRoutingConfiguration['matchMode'] = 'AllMatches'): LogStreamRoutingConfiguration {
    return {matchMode, routes};
}

function event(over: Partial<LogStreamEvent> = {}): LogStreamEvent {
    return {
        streamId: 'stream-1',
        sequence: 1,
        timestampUtc: '2026-01-01T00:00:00.000Z',
        loggerCategory: 'App.Worker',
        level: Level.INFO,
        eventId: 0,
        ...over,
    };
}

describe('resolveDestinations', () => {
    describe('logger name matching', () => {
        it('matches a prefix route on the category itself and on a dotted child', () => {
            const config = routing([route({loggerName: 'App', loggerNameMatchMode: 'Prefix'})]);

            expect(resolveDestinations(config, event({loggerCategory: 'App'}))).toHaveLength(1);
            expect(resolveDestinations(config, event({loggerCategory: 'App.Worker'}))).toHaveLength(1);
        });

        it('does not let a prefix route match a longer name that merely starts with it', () => {
            // The '.' is the whole point: "App" must not swallow "Application".
            const config = routing([route({loggerName: 'App', loggerNameMatchMode: 'Prefix'})]);

            expect(resolveDestinations(config, event({loggerCategory: 'Application'}))).toHaveLength(0);
        });

        it('matches exactly, ignoring case', () => {
            const config = routing([route({loggerName: 'App.Worker', loggerNameMatchMode: 'Exact'})]);

            expect(resolveDestinations(config, event({loggerCategory: 'app.worker'}))).toHaveLength(1);
            expect(resolveDestinations(config, event({loggerCategory: 'App.Worker.Inner'}))).toHaveLength(0);
        });

        it('matches anywhere in the name in Contains mode', () => {
            const config = routing([route({loggerName: 'ork', loggerNameMatchMode: 'Contains'})]);

            expect(resolveDestinations(config, event({loggerCategory: 'App.Worker'}))).toHaveLength(1);
            expect(resolveDestinations(config, event({loggerCategory: 'App.Reader'}))).toHaveLength(0);
        });
    });

    describe('level range', () => {
        it('excludes an event outside the route levels', () => {
            const config = routing([route({minLevel: Level.WARN, maxLevel: Level.ERROR})]);

            expect(resolveDestinations(config, event({level: Level.INFO}))).toHaveLength(0);
            expect(resolveDestinations(config, event({level: Level.WARN}))).toHaveLength(1);
            expect(resolveDestinations(config, event({level: Level.ERROR}))).toHaveLength(1);
        });

        it('treats an absent bound as unbounded', () => {
            const config = routing([route({minLevel: null, maxLevel: null})]);

            expect(resolveDestinations(config, event({level: Level.DEBUG}))).toHaveLength(1);
        });
    });

    describe('match mode', () => {
        const two = [
            route({order: 0, loggerName: 'App', loggerNameMatchMode: 'Prefix', destinations: [{category: fixed('Broad'), name: fixed('S')}]}),
            route({order: 1, loggerName: 'App.Worker', loggerNameMatchMode: 'Prefix', destinations: [{category: fixed('Narrow'), name: fixed('S')}]}),
        ];

        it('AllMatches uses every matching route', () => {
            const resolved = resolveDestinations(routing(two, 'AllMatches'), event());
            expect(resolved.map(d => d.category).sort()).toEqual(['Broad', 'Narrow']);
        });

        it('FirstMatch uses the first route in order', () => {
            const resolved = resolveDestinations(routing(two, 'FirstMatch'), event());
            expect(resolved.map(d => d.category)).toEqual(['Broad']);
        });

        it('MostSpecific uses the longest pattern', () => {
            const resolved = resolveDestinations(routing(two, 'MostSpecific'), event());
            expect(resolved.map(d => d.category)).toEqual(['Narrow']);
        });

        it('stopProcessing ends the scan at the route that set it', () => {
            const stopping = [
                route({order: 0, destinations: [{category: fixed('First'), name: fixed('S')}], stopProcessing: true}),
                route({order: 1, destinations: [{category: fixed('Second'), name: fixed('S')}]}),
            ];

            const resolved = resolveDestinations(routing(stopping, 'AllMatches'), event());
            expect(resolved.map(d => d.category)).toEqual(['First']);
        });
    });

    describe('LoggerSuffix values', () => {
        it('takes the whole logger name under a wildcard route', () => {
            const config = routing([route({destinations: [{category: fixed('Cat'), name: suffix}]})]);

            expect(resolveDestinations(config, event({loggerCategory: 'App.Worker'})))
                .toEqual([{category: 'Cat', name: 'App.Worker'}]);
        });

        it('takes the part below the pattern under a prefix route', () => {
            const config = routing([route({
                loggerName: 'App',
                loggerNameMatchMode: 'Prefix',
                destinations: [{category: fixed('Cat'), name: suffix}],
            })]);

            expect(resolveDestinations(config, event({loggerCategory: 'App.Worker.Inner'})))
                .toEqual([{category: 'Cat', name: 'Worker.Inner'}]);
        });

        it('places nothing when the name has no part below the pattern', () => {
            // The route matches, but there is no suffix to name the sink with.
            const config = routing([route({
                loggerName: 'App',
                loggerNameMatchMode: 'Prefix',
                destinations: [{category: fixed('Cat'), name: suffix}],
            })]);

            expect(resolveDestinations(config, event({loggerCategory: 'App'}))).toHaveLength(0);
        });
    });

    it('deduplicates destinations two routes agree on', () => {
        const config = routing([
            route({order: 0}),
            route({order: 1}),
        ]);

        expect(resolveDestinations(config, event())).toHaveLength(1);
    });

    it('places nothing when there is no routing at all', () => {
        expect(resolveDestinations(undefined, event())).toHaveLength(0);
    });
});
