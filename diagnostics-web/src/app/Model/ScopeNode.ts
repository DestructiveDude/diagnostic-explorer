
export class ScopeNode {
    displayText: string = '';
    firstLine: string = '';
    isBegin: boolean = false;
    level: number = 1;
    expanded = false;

    childRegions: ScopeNode[] = [];
    parentRegion?: ScopeNode;

    constructor(txt: string, isBegin: boolean = false) {
        if (isBegin) {
            this.firstLine = txt;
            this.isBegin = true;
        } else {
            this.displayText = txt;
        }
    }

    public toggleExpanded(): void {
        this.expanded = !this.expanded;
    }

    public addLine(txt: string) {
        this.displayText += '\r\n' + txt;
    }

    public hasChildren(): boolean {
        return this.childRegions.length > 0;
    }

    public getDisplayText(): string {
        return this.displayText != '' && !this.hasChildren() ? this.displayText : this.firstLine;
    }

    public addChild(reg: ScopeNode) {
        this.childRegions.push(reg);
        reg.level = this.level + 1;
        reg.parentRegion = this;
    }

    public static parseTraceScope(displayText: string): ScopeNode | undefined {
        let regions: ScopeNode[] = [];
        let scopePattern = /\[\d{2}\.\d{3}] \[\d{2}\.\d{3}] BEGIN.*/gs;
        let result = displayText.match(scopePattern);
        if (!result?.length)
            return undefined;

        displayText = result[0];

        const displayLines = displayText.split(/\r\n|\r|\n/);

        var curCollapsibleRegion = undefined;

        for (let i = 0; i < displayLines.length; i++) {
            let dl = displayLines[i].trim();
            // Iterate every line: the old `length - 1` bound dropped the final line (typically the
            // outermost END) whenever the input had no trailing newline. Skip blank lines explicitly
            // so a trailing empty split element no longer produces a spurious top-level node.
            if (!dl) continue;

            if (dl === '')
                continue;

            if (dl.indexOf("] BEGIN") > -1) {
                let newRegion = new ScopeNode(dl, true);

                /**
                 *  If the current block is a BEGIN, then add this as a child
                 */
                if (curCollapsibleRegion?.isBegin) {
                    curCollapsibleRegion.addChild(newRegion);
                }
                /**
                 *  If not, then add it as a child if the current block's parent is a BEGIN or
                 *  Add it to the overall list of text blocks
                 */
                else if (curCollapsibleRegion) {
                    curCollapsibleRegion = curCollapsibleRegion?.parentRegion;

                    if (curCollapsibleRegion?.isBegin) {
                        curCollapsibleRegion.addChild(newRegion);
                    } else {
                        regions.push(newRegion);
                    }
                }

                /**
                 * No parent BEGIN, so add to the overall list of text blocks
                 */
                if (!curCollapsibleRegion) {
                    regions.push(newRegion);
                }

                /**
                 * If both BEGIN and END, do not set it as the current block
                 */
                if (dl.indexOf("BEGIN/END") == -1) {
                    curCollapsibleRegion = newRegion;
                }
            } else if (curCollapsibleRegion && dl.indexOf("] END") > -1) {
                /**
                 * Always go up to the parent on an END unless this is a childless BEGIN
                 */
                if (!curCollapsibleRegion?.isBegin) {
                    curCollapsibleRegion = curCollapsibleRegion?.parentRegion;
                }
                /**
                 * Always go up a level on an END
                 */
                curCollapsibleRegion = curCollapsibleRegion?.parentRegion;

                let newRegion = new ScopeNode(dl);

                /**
                 * If parent exists, add as child.
                 */
                if (curCollapsibleRegion) {
                    curCollapsibleRegion.addChild(newRegion);
                }
                /**
                 * If no parent, add to the overall block list.
                 */
                else {
                    regions.push(newRegion);
                    curCollapsibleRegion = newRegion;
                }
            } else if (curCollapsibleRegion) {
                /**
                 * This is not a BEGIN or END, but if the current block is; need to add this as a child
                 */
                if (curCollapsibleRegion.isBegin) {
                    let newRegion = new ScopeNode(dl);
                    curCollapsibleRegion.addChild(newRegion);
                    curCollapsibleRegion = newRegion;
                }
                /**
                 * If not, just append text to the existing child block
                 */
                else {
                    curCollapsibleRegion.addLine(dl);
                }
            } else {
                /**
                 * If you made it here, this is automatically a top level block
                 */
                let newRegion = new ScopeNode(dl);
                regions.push(newRegion);

                curCollapsibleRegion = newRegion;
            }
        }

        return regions[0];
    }
}
