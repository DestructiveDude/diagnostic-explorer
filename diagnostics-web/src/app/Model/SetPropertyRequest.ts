export class SetPropertyRequest {
    id = '';
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
