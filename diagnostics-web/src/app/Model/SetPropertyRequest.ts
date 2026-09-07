export class SetPropertyRequest {
    id = '';
    /**
     * The operator action this is an attempt at. A retry of the SAME action must reuse the id:
     * that is what makes the agent join the first attempt rather than run the edit twice.
     */
    requestId = '';
    /** The drilldown the edit was made in, empty for the main view. */
    objectPaths: string[] = [];
    path = '';
    value = '';
}

export class OperationResponse {
    isSuccess = false;
    result = '';
    errorMessage = '';
    errorDetail = '';
}
