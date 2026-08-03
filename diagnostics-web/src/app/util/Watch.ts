import {SimpleChange, SimpleChanges} from '@angular/core';
import _ from 'lodash';

export function Watch(
    method: ((arg: any, changes: SimpleChanges) => void),
    comparison: (x: any, y: any) => boolean = _.eq): PropertyDecorator & MethodDecorator {

    return (target: any, key: string | symbol, propDesc?: PropertyDescriptor) => {
        let privateKey = "_" + key.toString();
        let isNotFirstChangePrivateKey = "_" + key.toString() + 'IsNotFirstChange';

        propDesc = propDesc || {
            configurable: true,
            enumerable: true,
        };
        propDesc.get = propDesc.get || (function (this: any) {
            return this[privateKey]
        });

        const originalSetter = propDesc.set || (function (this: any, val: any) {
            this[privateKey] = val
        });

        propDesc.set = function (this: any, val: any) {
            let oldValue = this[key];
            if (!comparison(val, oldValue)) {
                originalSetter.call(this, val);
                let isNotFirstChange = this[isNotFirstChangePrivateKey];
                this[isNotFirstChangePrivateKey] = true;

                if (this.watchEnabled === true) {
                    let changes: SimpleChanges = {
                        [key]: new SimpleChange(oldValue, val, !isNotFirstChange)
                    }
                    method(this, changes);
                }
            }
        }
        return propDesc;
    }
}
