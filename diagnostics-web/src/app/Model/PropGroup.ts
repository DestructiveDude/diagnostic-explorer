import {PropModel} from './PropModel';
import {Category} from './DiagResponse';
import {customMerge} from '../util/Merge';
import {SubCat} from './SubCat';

export class PropGroup {
    subCat: SubCat;
    name = '';
    canDrillDown = false;

    getPropertyPath(): string {
        return [this.subCat.getPropertyPath(), this.name].join('|');
    }
    properties: PropModel[] = [];

    constructor(subCat: SubCat, propCat: Category) {
        this.subCat = subCat;
        this.name = propCat.name;
        this.update(propCat);
    }

    update(propCat: Category) {
        this.canDrillDown = propCat.canDrillDown ?? false;
        this.properties = customMerge(propCat.properties,
            this.properties,
            s => s.name,
            t => t.name,
            s => new PropModel(this, s),
            (s, t) => t.update(s));
    }
}
