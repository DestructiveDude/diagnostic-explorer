export class ExecOperationRequest {
    id = '';
    /** The drilldown the operation was triggered in, empty for the main view. */
    objectPaths: string[] = [];
    path = '';
    operation = '';
    arguments: string[] = [];
}
