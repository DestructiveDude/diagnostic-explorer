export function customMerge<T, S, K>(
    source: S[],
    target: T[],
    sourceKey: (s: S) => K,
    targetKey: (t: T) => K,
    create: (s: S) => T,
    update: (s: S, t: T) => void,
    remove = true): T[] {
    const mapSource: Map<K, S> = new Map<K, S>();
    source.forEach(x => mapSource.set(sourceKey(x), x));

    const mapTarget: Map<K, T> = new Map<K, T>();
    target.forEach(x => mapTarget.set(targetKey(x), x));

    let altered = false;

    if (remove) {
        for (let index: number = target.length - 1; index >= 0; index--) {
            if (!mapSource.has(targetKey(target[index]))) {
                target.splice(index, 1);
                altered = true;
            }
        }
    }

    source.forEach(item => {
        const existing = mapTarget.get(sourceKey(item));
        if (existing == null) {
            target.push(create(item));
            altered = true;
        } else {
            update(item, existing);
        }
    });

    return altered ? target.slice() : target;
}

export function simpleMerge<T, K>(
    source: T[],
    target: T[],
    keyGen: (x: T) => K,
    update?: (s: T, t: T) => void): T[] {

    if (!update)
        update = (_s, _t) => {
        };

    return customMerge<T, T, K>(source, target, keyGen, keyGen, s => s, update);
}
