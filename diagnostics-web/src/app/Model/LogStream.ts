import {SystemEvent} from './DiagResponse';

/**
 * The realtime log stream as it arrives from the service.
 *
 * Enum-valued fields are strings on this channel: the service registers a
 * JsonStringEnumConverter on the SignalR JSON protocol, so `matchMode` arrives as
 * `'AllMatches'` rather than `0`.
 */

export type EventSinkRouteMatchMode = 'AllMatches' | 'MostSpecific' | 'FirstMatch';

export type LoggerNameMatchMode = 'Exact' | 'Prefix' | 'Contains' | 'Wildcard';

export type RouteValueSource = 'Fixed' | 'LoggerSuffix';

export interface LogStreamEvent {
    streamId: string;
    sequence: number;
    timestampUtc: string;
    loggerCategory: string;
    level: number;
    message?: string;
    detail?: string;
    eventId: number;
    eventName?: string;
}

export interface LogStreamRouteValue {
    source: RouteValueSource;
    /** Absent when `source` derives the value from the logger name. */
    value?: string;
}

export interface LogStreamRouteDestination {
    category: LogStreamRouteValue;
    name: LogStreamRouteValue;
}

export interface LogStreamRoute {
    order: number;
    loggerName: string;
    loggerNameMatchMode: LoggerNameMatchMode;
    minLevel?: number | null;
    maxLevel?: number | null;
    stopProcessing: boolean;
    destinations: LogStreamRouteDestination[];
}

export interface LogStreamRoutingConfiguration {
    matchMode: EventSinkRouteMatchMode;
    routes: LogStreamRoute[];
}

export interface LogStreamInitialization {
    streamId: string;
    routing: LogStreamRoutingConfiguration;
    replayEvents: LogStreamEvent[];
    highWatermark: number;
    maxEvents: number;
    maxAgeMinutes: number;
}

/** Where an event is displayed: the sink category and name it routes to. */
export interface EventDestination {
    category: string;
    name: string;
}

/**
 * Resolves the destinations an event is displayed under.
 *
 * The agent's router decides only whether to publish an event; it does not stamp a destination
 * onto it, because one event can belong to several and the routing can change without the events
 * changing. So the routing snapshot travels with the stream and the destination is worked out
 * here, which also lets the UI show a configured destination that has no events yet.
 *
 * This mirrors EventSinkRouter on the agent, and the two must agree: a route matches on level
 * range first, then on logger name, and `stopProcessing` ends the scan at the route that set it.
 */
export function resolveDestinations(
    routing: LogStreamRoutingConfiguration | undefined,
    event: LogStreamEvent
): EventDestination[] {
    const matches: LogStreamRoute[] = [];
    for (const route of routing?.routes ?? []) {
        if (routeMatches(route, event)) {
            matches.push(route);
            if (route.stopProcessing) break;
        }
    }

    const selected = selectRoutes(routing?.matchMode, matches);
    const destinations = new Map<string, EventDestination>();
    for (const route of selected) {
        for (const destination of route.destinations ?? []) {
            const category = resolveValue(destination.category, route, event.loggerCategory);
            const name = resolveValue(destination.name, route, event.loggerCategory);
            if (category && name)
                destinations.set(destinationKey(category, name), {category, name});
        }
    }

    return [...destinations.values()];
}

export function destinationKey(category: string, name: string): string {
    // Written as an escape rather than a literal control character so it survives an edit that
    // strips non-printables. Without a separator ('ab','c') and ('a','bc') would be one key.
    return `${category}\u001f${name}`.toLocaleLowerCase();
}

/**
 * Converts a wire level to the scale the UI displays on.
 *
 * These are two different vocabularies and neither is wrong. The wire carries a
 * Microsoft.Extensions.Logging ordinal (0..6), because that is what every adapter produces and
 * what a route's minLevel/maxLevel are compared against on both sides. The UI's `Level` is
 * log4net's scale (10 000..120 000), which is what the severity colours, the category roll-up and
 * the filter all read.
 *
 * Passing the ordinal through unmapped puts every event below `Level.VERBOSE`, so it renders as
 * 'Unknown' with no severity at all - and Trace, being 0, reads as "no level set" and was being
 * shown as an Error.
 *
 * This is the inverse of LogLevelMap.ToMicrosoftOrdinal on the agent, which collapses log4net's
 * twelve levels into seven. That collapse is lossy, so each ordinal maps back to the
 * representative level of its band rather than to whatever it started as.
 */
export function toDisplayLevel(wireLevel: number | undefined | null): number {
    switch (wireLevel) {
        case 0: return 20_000;  // Trace       (band: All/Verbose/Trace)
        case 1: return 30_000;  // Debug
        case 2: return 40_000;  // Information (band: Info/Notice)
        case 3: return 60_000;  // Warning
        case 4: return 70_000;  // Error
        case 5: return 90_000;  // Critical    (band: Severe/Critical/Alert/Fatal/Emergency)
        case 6: return 40_000;  // None - nothing logs at this level; show it rather than hide it.
        default:
            // Not a level this contract defines. Surface it rather than let it sink to the bottom
            // of the scale and disappear.
            return 70_000;      // Error
    }
}

export function toSystemEvent(event: LogStreamEvent, sinkCategory = '', sinkName = ''): SystemEvent {
    return Object.assign(new SystemEvent(), {
        id: event.sequence,
        date: Date.parse(event.timestampUtc),
        message: event.message ?? '',
        detail: event.detail ?? '',
        level: toDisplayLevel(event.level),
        sinkCategory,
        sinkName
    });
}

export function routeMatches(
    route: Pick<LogStreamRoute, 'loggerName' | 'loggerNameMatchMode' | 'minLevel' | 'maxLevel'>,
    event: LogStreamEvent
): boolean {
    if (route.minLevel != null && event.level < route.minLevel) return false;
    if (route.maxLevel != null && event.level > route.maxLevel) return false;

    const loggerName = event.loggerCategory ?? '';
    const matcher = route.loggerName ?? '';
    switch (route.loggerNameMatchMode) {
        case 'Exact':
            return equalsCI(loggerName, matcher);
        case 'Prefix':
            // Equal, or a dotted child of it. The explicit '.' is what stops "Foo" matching
            // "Foobar" — the same rule EventSinkRouter.Matches applies on the agent.
            return equalsCI(loggerName, matcher)
                || (loggerName.length > matcher.length
                    && loggerName.toLocaleLowerCase().startsWith(`${matcher.toLocaleLowerCase()}.`));
        case 'Contains':
            return loggerName.toLocaleLowerCase().includes(matcher.toLocaleLowerCase());
        case 'Wildcard':
            return true;
        default:
            return false;
    }
}

function selectRoutes(matchMode: EventSinkRouteMatchMode | undefined, matches: LogStreamRoute[]): LogStreamRoute[] {
    if (matches.length === 0) return [];
    switch (matchMode) {
        case 'MostSpecific':
            // Longest pattern wins; ties go to the route declared first.
            return [[...matches].sort((left, right) =>
                right.loggerName.length - left.loggerName.length || left.order - right.order)[0]];
        case 'FirstMatch':
            return [matches[0]];
        default:
            return matches;
    }
}

function resolveValue(
    value: LogStreamRouteValue | undefined,
    route: LogStreamRoute,
    loggerName: string
): string | undefined {
    if (!value) return undefined;
    if (value.source === 'Fixed') return value.value;

    // LoggerSuffix: the part of the logger name below the route's pattern. A wildcard route has no
    // pattern to strip, so the whole name is the suffix; any other mode leaves nothing meaningful
    // to take, and an event with no resolvable value is simply not placed under that destination.
    if (route.loggerNameMatchMode === 'Wildcard') return loggerName;
    if (route.loggerNameMatchMode !== 'Prefix' || loggerName.length <= route.loggerName.length) return undefined;
    return loggerName.substring(route.loggerName.length + 1);
}

function equalsCI(left: string, right: string): boolean {
    return left.toLocaleLowerCase() === right.toLocaleLowerCase();
}
