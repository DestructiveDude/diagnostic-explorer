import {ScopeNode} from './ScopeNode';

describe('ScopeNode', () => {
    it('includes the final non-empty line in a parsed trace scope', () => {
        const scope = ScopeNode.parseTraceScope(
            '[00.000] [00.000] BEGIN Root\n' +
            '[00.001] [00.001] first line\n' +
            '[00.002] [00.002] final line'
        );

        expect(scope).toBeDefined();
        expect(scope!.childRegions).toHaveLength(1);
        expect(scope!.childRegions[0].displayText).toContain('final line');
    });

    it('does not create a blank trailing node for a trailing newline', () => {
        const scope = ScopeNode.parseTraceScope(
            '[00.000] [00.000] BEGIN Root\n' +
            '[00.001] [00.001] only line\n'
        );

        expect(scope).toBeDefined();
        expect(scope!.childRegions).toHaveLength(1);
        expect(scope!.childRegions[0].displayText).toBe('[00.001] [00.001] only line');
    });
});
