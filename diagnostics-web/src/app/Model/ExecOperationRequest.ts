export class ExecOperationRequest {
    id = '';
    /** @see SetPropertyRequest.requestId */
    requestId = '';
    /** The drilldown the operation was triggered in, empty for the main view. */
    objectPaths: string[] = [];
    path = '';
    operation = '';
    arguments: string[] = [];
}
