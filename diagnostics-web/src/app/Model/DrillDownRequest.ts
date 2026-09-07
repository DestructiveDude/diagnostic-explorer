import {Null} from '../util/Null';
import {DiagnosticResponse} from './DiagResponse';
import {LoggerNameMatchMode} from './LogStream';

/**
 * Addresses a value inside a process's diagnostics so it can be inspected in its own right.
 *
 * The value is named by an ordered chain of ordinary diagnostic paths, not by a handle the agent
 * has to keep alive between calls: each entry resolves against the diagnostics the previous one
 * produced. A chain that no longer resolves fails as a lookup rather than returning a stale object.
 */
export class DrillDownRequest {
    id = '';
    objectPaths: string[] = [];

    /** Return the value serialised as JSON instead of as diagnostics. */
    jsonHover = false;

    /** Skip resolving event views, for a caller that only wants the properties. */
    excludeEventViews = false;
}

/**
 * Admits events by logger name and level. Field-compatible with the matching part of
 * {@link LogStreamRoute}, so the same matching logic serves both.
 */
export class DrillDownEventMatcher {
    loggerName = '';
    loggerNameMatchMode: LoggerNameMatchMode = 'Exact';
    minLevel: Null<number> = null;
    maxLevel: Null<number> = null;
}

/**
 * One event table a drilldown offers, as a projection over events the client already receives.
 * A definition, not a subscription: opening a drilldown starts no new stream and cannot widen
 * what the process captures.
 */
export class DrillDownEventViewDefinition {
    id = '';
    category = '';
    name = '';
    matchers: DrillDownEventMatcher[] = [];
}

export class DrillDownResponse {
    diagnostics: DiagnosticResponse = new DiagnosticResponse();

    /** How many items the response carries, which is 1 for a single object. */
    displayedCount = 0;

    /** The collection's own count where it has one, else null. */
    totalCount: Null<number> = null;

    isTruncated = false;
    errorMessage: Null<string> = null;
    errorDetail: Null<string> = null;
    eventViews: DrillDownEventViewDefinition[] = [];

    /** Set only for a jsonHover request. */
    json: Null<string> = null;
}
