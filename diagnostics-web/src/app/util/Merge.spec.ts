import {customMerge, simpleMerge} from './Merge';

interface Item {
    id: number;
    value: string;
}

const byId = (x: Item) => x.id;
const create = (s: Item): Item => ({...s});
const copyValue = (s: Item, t: Item) => {
    t.value = s.value;
};
const noop = () => {
};

describe('customMerge', () => {
    it('drops target items that are absent from the source', () => {
        const target: Item[] = [{id: 1, value: 'a'}, {id: 2, value: 'b'}, {id: 3, value: 'c'}];
        const source: Item[] = [{id: 2, value: 'b'}, {id: 3, value: 'c'}];

        const result = customMerge(source, target, byId, byId, create, noop);

        expect(result.map(x => x.id)).toEqual([2, 3]);
    });

    it('keeps the existing item object when its key is still present', () => {
        const existing: Item = {id: 1, value: 'old'};
        const target: Item[] = [existing];
        const source: Item[] = [{id: 1, value: 'new'}];

        const result = customMerge(source, target, byId, byId, create, copyValue);

        // The matched target instance is updated in place, not replaced by create().
        expect(result[0]).toBe(existing);
        expect(result[0].value).toBe('new');
    });

    it('appends source items with no matching target key via the create callback', () => {
        const target: Item[] = [{id: 1, value: 'a'}];
        const source: Item[] = [{id: 1, value: 'a'}, {id: 2, value: 'b'}];

        const result = customMerge(source, target, byId, byId, create, noop);

        expect(result.map(x => x.id)).toEqual([1, 2]);
        expect(result[1]).toEqual({id: 2, value: 'b'});
    });

    it('returns the same array reference when nothing was added or removed', () => {
        const target: Item[] = [{id: 1, value: 'a'}, {id: 2, value: 'b'}];
        const source: Item[] = [{id: 1, value: 'a'}, {id: 2, value: 'b'}];

        const result = customMerge(source, target, byId, byId, create, copyValue);

        // Reference preservation is the change-detection contract: an unchanged
        // merge must not force Angular to re-render the list.
        expect(result).toBe(target);
    });

    it('returns a new array reference when an item was added', () => {
        const target: Item[] = [{id: 1, value: 'a'}];
        const source: Item[] = [{id: 1, value: 'a'}, {id: 2, value: 'b'}];

        const result = customMerge(source, target, byId, byId, create, noop);

        expect(result).not.toBe(target);
        expect(result.map(x => x.id)).toEqual([1, 2]);
    });

    it('returns a new array reference when an item was removed', () => {
        const target: Item[] = [{id: 1, value: 'a'}, {id: 2, value: 'b'}];
        const source: Item[] = [{id: 1, value: 'a'}];

        const result = customMerge(source, target, byId, byId, create, noop);

        expect(result).not.toBe(target);
        expect(result.map(x => x.id)).toEqual([1]);
    });
});

describe('simpleMerge', () => {
    it('reconciles by key, removing stale and appending new items', () => {
        const target = ['a', 'b'];
        const source = ['b', 'c'];

        const result = simpleMerge(source, target, x => x);

        expect(result).toEqual(['b', 'c']);
    });
});
