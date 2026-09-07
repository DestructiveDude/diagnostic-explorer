import {Property} from './DiagResponse';
import {PropGroup} from './PropGroup';
import _ from 'lodash';

export class PropModel {
    group: PropGroup;
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

    constructor(group: PropGroup, source: Property) {
        this.group = group;
        this.name = source.name;
        this.update(source);
    }

    update(source: Property): void {
        this.value = source.value;
        this.description = source.description;
        this.operationSet = source.operationSet;
        this.canSet = source.canSet;
        this.canDrillDown = source.canDrillDown ?? false;
        this.drillDownIconOnly = source.drillDownIconOnly ?? false;
        this.canJsonHover = source.canJsonHover ?? false;
        this.canExpandedHover = source.canExpandedHover ?? false;
        this.drillDownText = source.drillDownText ?? '';
    }

    getPropertyPath(): string {
        const pathElements = [
            this.group.subCat.cat.name,
            this.group.subCat.name,
            this.group.name,
            this.name];

        return _(pathElements).join('|');
    }
}
