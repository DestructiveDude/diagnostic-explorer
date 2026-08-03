import {Component, ElementRef, HostListener, ViewChild, ChangeDetectionStrategy} from '@angular/core';
import {Table} from 'primeng/table';
import {AppModel} from '../Model/AppModel';
import {DiagProcess} from '../Model/DiagProcess';
import {RealtimeModel} from '../Model/RealtimeModel';
import {MenuItem} from 'primeng/api';

@Component({
    selector: 'app-realtime-nav',
    templateUrl: './realtime-nav.component.html',
    styleUrls: ['./realtime-nav.component.scss'],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class RealtimeNavComponent {

    @ViewChild(Table) primeTable!: Table;

    selectedProcess?: DiagProcess;

    readonly contextMenuItems: MenuItem[] = [
        {label: 'Retro', command: () => this.selectedProcess && this.app.showRetro(this.selectedProcess)},
        {label: 'Delete', command: () => this.selectedProcess && this.model.deleteProcess(this.selectedProcess)},
    ];

    constructor(readonly app: AppModel, readonly model: RealtimeModel, private hostRef: ElementRef) {}

    getProcess(item: any): DiagProcess {
        return item as DiagProcess;
    }

    // Use bounding rects instead of event.target — PrimeNG retargets target to <p-table>.
    @HostListener('dblclick', ['$event'])
    onDblClick(event: MouseEvent): void {
        const el = this.hostRef.nativeElement as HTMLElement;
        const thead = el.querySelector<HTMLElement>('thead');
        if (!thead) return;
        const tr = thead.getBoundingClientRect();
        if (event.clientY < tr.top || event.clientY > tr.bottom) return;
        const ths = Array.from(el.querySelectorAll<HTMLElement>('thead tr th'));
        const idx = ths.findIndex(th => {
            const r = th.getBoundingClientRect();
            return event.clientX >= r.left && event.clientX <= r.right;
        });
        if (idx >= 0) this.fitColumn(idx, ths);
    }

    onOnlineOnlyChange(val: boolean): void {
        this.model.onlineOnly = val;
        requestAnimationFrame(() => this.fitAllIfRoom());
    }

    private fitColumn(colIndex: number, ths: HTMLElement[]): void {
        const el = this.hostRef.nativeElement as HTMLElement;
        let maxW = this.measureCell(ths[colIndex]);
        el.querySelectorAll<HTMLElement>('tbody tr').forEach(row => {
            const td = row.querySelectorAll<HTMLElement>('td')[colIndex];
            if (td) maxW = Math.max(maxW, this.measureCell(td));
        });
        // Keep current widths for other columns, only update this one
        const widths = ths.map((th, i) =>
            i === colIndex ? maxW : (parseFloat(th.style.width) || th.offsetWidth)
        );
        this.applyWidths(widths);
    }

    private fitAllIfRoom(): void {
        const el = this.hostRef.nativeElement as HTMLElement;
        const tableEl = el.querySelector<HTMLElement>('table');
        const ths = Array.from(el.querySelectorAll<HTMLElement>('thead tr th'));
        if (!tableEl || !ths.length) return;

        // Only auto-fit if at least one column is currently overflowing its content
        const cells = Array.from(el.querySelectorAll<HTMLElement>('thead tr th, tbody tr td'));
        const hasOverflow = cells.some(c => c.scrollWidth > c.offsetWidth + 1);
        if (!hasOverflow) return;

        const colWidths = ths.map((th, i) => {
            let maxW = this.measureCell(th);
            el.querySelectorAll<HTMLElement>('tbody tr').forEach(row => {
                const td = row.querySelectorAll<HTMLElement>('td')[i];
                if (td) maxW = Math.max(maxW, this.measureCell(td));
            });
            return maxW;
        });
        if (colWidths.reduce((a, b) => a + b, 0) <= tableEl.offsetWidth) {
            this.applyWidths(colWidths);
        }
    }

    private applyWidths(widths: number[]): void {
        if (!this.primeTable) return;
        const pt = this.primeTable as any;
        pt.columnWidthsState = widths.join(',');
        pt.restoreColumnWidths();
        if (pt.stateKey) {
            try {
                const raw = localStorage.getItem(pt.stateKey);
                const state = raw ? JSON.parse(raw) : {};
                state.columnWidths = widths.join(',');
                localStorage.setItem(pt.stateKey, JSON.stringify(state));
            } catch {}
        }
    }

    private _canvas?: HTMLCanvasElement;

    private measureCell(el: HTMLElement): number {
        if (!this._canvas) this._canvas = document.createElement('canvas');
        const ctx = this._canvas.getContext('2d')!;
        const style = window.getComputedStyle(el);
        ctx.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
        const text = el.textContent?.trim() ?? '';
        const pad = parseFloat(style.paddingLeft) + parseFloat(style.paddingRight);
        return Math.ceil(ctx.measureText(text).width) + pad + 8;
    }
}
