import {Null} from '../util/Null';

export class DiagnosticResponse {
    propertyBags: PropertyBag[] = [];
    events: EventResponse[] = [];
    operationSets: OperationSet[] = [];
    context: Null<string> = null;
    exceptionMessage: Null<string> = null;
    exceptionDetail: Null<string> = null;
}

export class PropertyBag {
    canDrillDown = false;
    name: string = '';
    category: string = '';
    operationSet: string = '';
    categories: Category[] = [];
}

export class Category {
    canDrillDown = false;
    name = '';
    operationSet = '';
    properties: Property[] = [];
}

export class Property {
    name = '';
    value = '';
    description = '';
    operationSet = '';
    canSet = false;
    canDrillDown = false;
    drillDownIconOnly = false;
    canJsonHover = false;
    canExpandedHover = false;
    drillDownText = '';
}

export class EventResponse {
    name = '';
    category = '';
    events: SystemEvent[] = []
}

export class SystemEvent {
    id = 0;
    date = Date.parse('1 Jan 2000');
    message = '';
    detail = '';
    level = 0;
    sinkName = '';
    sinkCategory = '';
}


export class OperationSet {
    id = '';
    operations: Operation[] = [];
}

export class Operation {
    returnType = ''
    signature = '';
    description = '';
    parameters: OperationParameter[] = [];
}


export class OperationParameter {
    name = '';
    type = '';
}
