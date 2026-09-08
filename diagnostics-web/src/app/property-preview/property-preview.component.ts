import {afterNextRender, ChangeDetectionStrategy, Component, DoCheck, EventEmitter, Injector, Input, OnDestroy, Output, ViewChild} from '@angular/core';
import {CdkConnectedOverlay, ConnectedPosition} from '@angular/cdk/overlay';
import {PropModel} from '../Model/PropModel';
import {DiagnosticResponse} from '../Model/DiagResponse';
import {DrillDownRequest, DrillDownResponse} from '../Model/DrillDownRequest';
import {RealtimeModel} from '../Model/RealtimeModel';
import {getErrorMessage} from '../util/util';

let nextTooltipId = 0;

function createTooltipId(): string {
    nextTooltipId += 1;
    return 'property-preview-' + nextTooltipId;
}

@Component({
    selector: 'app-property-preview',
    templateUrl: './property-preview.component.html',
    styleUrls: ['./property-preview.component.scss'],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class PropertyPreviewComponent implements DoCheck, OnDestroy {
    readonly tooltipId = createTooltipId();
    readonly positions: ConnectedPosition[] = [
        {originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'top', offsetY: 6},
        {originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom', offsetY: -6},
        {originX: 'end', originY: 'bottom', overlayX: 'end', overlayY: 'top', offsetY: 6},
        {originX: 'end', originY: 'top', overlayX: 'end', overlayY: 'bottom', offsetY: -6}
    ];

    @Input({required: true}) prop!: PropModel;
    @Input() json = false;
    @Input({required: true}) processId = '';
    @Input() objectPaths: string[] = [];
    @Output() inspect = new EventEmitter<void>();
    @ViewChild(CdkConnectedOverlay) private overlay?: CdkConnectedOverlay;

    visible = false;
    loading = false;
    error = '';
    diagnostics?: DiagnosticResponse;
    jsonText: string | null = null;
    response?: DrillDownResponse;

    private pointerOnTrigger = false;
    private pointerOnOverlay = false;
    private focused = false;
    private hideTimer?: number;
    private refreshTimer?: number;
    private pending = false;
    private opening = 0;
    private openedContext = '';
    private request?: DrillDownRequest;

    constructor(private readonly realtimeModel: RealtimeModel, private readonly injector: Injector) {}

    ngDoCheck(): void {
        if (this.visible && this.contextKey() !== this.openedContext) this.close();
    }

    ngOnDestroy(): void {
        this.close();
    }

    openFromPointer(): void {
        this.pointerOnTrigger = true;
        this.cancelHide();
        this.open();
    }

    leaveTrigger(): void {
        this.pointerOnTrigger = false;
        this.scheduleClose();
    }

    openFromFocus(): void {
        this.focused = true;
        this.cancelHide();
        this.open();
    }

    leaveFocus(): void {
        this.focused = false;
        this.scheduleClose();
    }

    enterOverlay(): void {
        this.pointerOnOverlay = true;
        this.cancelHide();
    }

    leaveOverlay(): void {
        this.pointerOnOverlay = false;
        this.scheduleClose();
    }

    inspectValue(): void {
        this.close();
        this.inspect.emit();
    }

    dismiss(event?: KeyboardEvent): void {
        if (event?.key !== 'Escape') return;
        event.preventDefault();
        this.close();
    }

    private open(): void {
        if (this.visible) return;

        this.visible = true;
        this.loading = true;
        this.error = '';
        this.diagnostics = undefined;
        this.jsonText = null;
        this.response = undefined;
        this.openedContext = this.contextKey();
        this.request = {
            ...new DrillDownRequest(), id: this.processId,
            objectPaths: [...this.objectPaths, this.prop.getPropertyPath()],
            jsonHover: this.json, excludeEventViews: true
        };
        this.opening += 1;
        const opening = this.opening;
        this.refreshTimer = window.setInterval(() => this.refresh(opening), 5000);
        this.refresh(opening);
    }

    private refresh(opening: number): void {
        if (!this.visible || this.pending || opening !== this.opening || !this.request) return;

        this.pending = true;
        this.load(opening, this.request);
    }

    private async load(opening: number, request: DrillDownRequest): Promise<void> {
        try {
            const response = await this.realtimeModel.hubService.getDrillDown(request);
            if (!this.isCurrent(opening)) return;
            this.response = response;
            const error = response.errorMessage || response.diagnostics.exceptionMessage;
            if (error) {
                this.showError(error);
                return;
            }
            this.error = '';
            if (this.json) {
                this.diagnostics = undefined;
                this.jsonText = this.formatJson(response.json);
            } else {
                this.jsonText = null;
                this.diagnostics = response.diagnostics;
            }
        } catch (error) {
            if (this.isCurrent(opening)) this.showError(getErrorMessage(error) || 'Unable to load preview');
        } finally {
            if (this.isCurrent(opening)) {
                this.loading = false;
                this.pending = false;
                this.reposition();
            }
        }
    }

    private showError(error: string): void {
        this.error = error;
        this.diagnostics = undefined;
        this.jsonText = null;
        this.response = undefined;
    }

    private formatJson(raw: string | null): string | null {
        if (!raw) return null;
        try {
            return JSON.stringify(JSON.parse(raw), null, 2);
        } catch {
            return raw;
        }
    }

    private scheduleClose(): void {
        this.cancelHide();
        if (this.pointerOnTrigger || this.pointerOnOverlay || this.focused) return;
        this.hideTimer = window.setTimeout(() => {
            if (!this.pointerOnTrigger && !this.pointerOnOverlay && !this.focused) this.close();
        }, 150);
    }

    private cancelHide(): void {
        if (this.hideTimer === undefined) return;
        window.clearTimeout(this.hideTimer);
        this.hideTimer = undefined;
    }

    close(): void {
        this.cancelHide();
        if (this.refreshTimer !== undefined) window.clearInterval(this.refreshTimer);
        this.refreshTimer = undefined;
        this.visible = false;
        this.loading = false;
        this.pending = false;
        this.request = undefined;
        this.opening++;
    }

    private isCurrent(opening: number): boolean {
        return this.visible && opening === this.opening && this.contextKey() === this.openedContext;
    }

    private contextKey(): string {
        return this.processId + '\u001e' + this.objectPaths.join('\u001e') + '\u001e' +
            (this.prop?.getPropertyPath() ?? '') + '\u001e' + this.json;
    }

    private reposition(): void {
        afterNextRender(() => this.overlay?.overlayRef?.updatePosition(), {injector: this.injector});
    }
}
