import {Component, ElementRef, ViewChild} from '@angular/core';
import {AppModel} from '../Model/AppModel';
import {DiagProcess} from '../Model/DiagProcess';
import {RealtimeModel} from '../Model/RealtimeModel';
import {MenuItem} from 'primeng/api';

@Component({
    selector: 'app-realtime-nav',
    templateUrl: './realtime-nav.component.html',
    styleUrls: ['./realtime-nav.component.scss'],
    standalone: false
})
export class RealtimeNavComponent {

    @ViewChild('dt', { read: ElementRef }) tableRef!: ElementRef;

    selectedProcess?: DiagProcess;

    readonly contextMenuItems: MenuItem[] = [
        {label: 'Retro', command: () => this.selectedProcess && this.app.showRetro(this.selectedProcess)},
        {label: 'Delete', command: () => this.selectedProcess && this.model.deleteProcess(this.selectedProcess)},
    ];

    constructor(readonly app: AppModel, readonly model: RealtimeModel) {}

    getProcess(item: any): DiagProcess {
        return item as DiagProcess;
    }

    onOnlineOnlyChange(val: boolean): void {
        this.model.onlineOnly = val;
        setTimeout(() => this.fitAllIfRoom(), 0);
    }

    fitColumn(colIndex: number): void {
        const el = this.tableRef?.nativeElement as HTMLElement;
        if (!el) return;
        const ths = el.querySelectorAll<HTMLElement>('thead tr th');
        const th = ths[colIndex];
        if (!th) return;
        let maxW = this.measureCell(th);
        el.querySelectorAll<HTMLElement>('tbody tr').forEach(row => {
            const td = row.querySelectorAll<HTMLElement>('td')[colIndex];
            if (td) maxW = Math.max(maxW, this.measureCell(td));
        });
        th.style.width = `${maxW}px`;
    }

    fitAllIfRoom(): void {
        const el = this.tableRef?.nativeElement as HTMLElement;
        if (!el) return;
        const tableEl = el.querySelector<HTMLElement>('table');
        if (!tableEl) return;
        const ths = Array.from(el.querySelectorAll<HTMLElement>('thead tr th'));
        if (!ths.length) return;
        const colWidths = ths.map((th, i) => {
            let maxW = this.measureCell(th);
            el.querySelectorAll<HTMLElement>('tbody tr').forEach(row => {
                const td = row.querySelectorAll<HTMLElement>('td')[i];
                if (td) maxW = Math.max(maxW, this.measureCell(td));
            });
            return maxW;
        });
        const totalNeeded = colWidths.reduce((a, b) => a + b, 0);
        if (totalNeeded <= tableEl.offsetWidth) {
            ths.forEach((th, i) => th.style.width = `${colWidths[i]}px`);
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
