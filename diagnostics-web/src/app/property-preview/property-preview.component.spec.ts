import {DatePipe} from '@angular/common';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {AppModule} from '../app.module';
import {PropertyBag, Category, DiagnosticResponse, Property} from '../Model/DiagResponse';
import {DrillDownResponse} from '../Model/DrillDownRequest';
import {RealtimeModel} from '../Model/RealtimeModel';
import {PropertyPreviewComponent} from './property-preview.component';

function response(value: string): DrillDownResponse {
    return Object.assign(new DrillDownResponse(), {
        isTruncated: true, displayedCount: 1, totalCount: 2,
        diagnostics: Object.assign(new DiagnosticResponse(), {
            propertyBags: [Object.assign(new PropertyBag(), {
                name: 'Orders[2]\u001fabcd1234', category: 'Trading',
                categories: [Object.assign(new Category(), {
                    name: 'State',
                    properties: [Object.assign(new Property(), {name: 'Price', value})]
                })]
            })]
        })
    });
}

describe('property preview', () => {
    let hub: {getDrillDown: jest.Mock};
    let fixture: ComponentFixture<PropertyPreviewComponent>;

    beforeEach(async () => {
        hub = {getDrillDown: jest.fn().mockResolvedValueOnce(response('10')).mockResolvedValueOnce(response('11'))};
        await TestBed.configureTestingModule({
            imports: [AppModule],
            providers: [{provide: RealtimeModel, useValue: {hubService: hub}}, DatePipe]
        }).compileComponents();
        fixture = TestBed.createComponent(PropertyPreviewComponent);
        fixture.componentRef.setInput('prop', {name: 'Order', getPropertyPath: () => 'Trading|Orders||Order'});
        fixture.componentRef.setInput('processId', 'original');
        fixture.componentRef.setInput('objectPaths', ['Outer']);
        fixture.detectChanges();
    });

    afterEach(() => {
        fixture.destroy();
        jest.useRealTimers();
    });

    async function settle(): Promise<void> {
        await Promise.resolve();
        await Promise.resolve();
        fixture.detectChanges();
    }

    it('renders grouped preview content and refreshes it after five seconds', async () => {
        jest.useFakeTimers();
        const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
        button.dispatchEvent(new FocusEvent('focus'));
        await settle();

        expect(hub.getDrillDown).toHaveBeenCalledTimes(1);
        expect(fixture.componentInstance.loading).toBe(false);
        expect(document.querySelector('[role="tooltip"]')?.textContent).toContain('Price');
        expect(document.querySelector('[role="tooltip"]')?.textContent).toContain('10');
        expect(document.querySelector('[role="tooltip"]')?.textContent).toContain('Orders[2]');
        expect(document.querySelector('[role="tooltip"]')?.textContent).toContain('Trading');
        expect(document.querySelector('[role="tooltip"]')?.textContent).not.toContain('abcd1234');
        expect(document.querySelector('[role="tooltip"]')?.textContent).toContain('Showing 1 of 2 items (truncated).');

        jest.advanceTimersByTime(5000);
        await settle();

        expect(hub.getDrillDown).toHaveBeenCalledTimes(2);
        expect(document.querySelector('[role="tooltip"]')?.textContent).toContain('11');
    });

    it('preserves server JSON exactly, preserves malformed JSON, and escapes remote markup', async () => {
        const json = '{\n  "id": 9223372036854775807,\n  "amount": 1234567890.1234567890123456789,\n  "overflow": 1e400,\n  "markup": "<script>bad()</script>"\n}';
        hub.getDrillDown.mockReset().mockResolvedValue(Object.assign(new DrillDownResponse(), {
            json, isTruncated: true, displayedCount: 2, totalCount: 3
        }));
        fixture.componentRef.setInput('json', true);
        fixture.detectChanges();
        expect(fixture.componentInstance.json).toBe(true);
        const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
        button.dispatchEvent(new FocusEvent('focus'));
        await settle();

        expect(document.querySelector('pre')?.textContent).toBe(json);
        expect(document.querySelector('[role="tooltip"] script')).toBeNull();
        expect(document.querySelector('[role="tooltip"]')?.textContent).toContain('Showing 2 of 3 items (truncated).');

        button.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape'}));
        fixture.componentRef.setInput('processId', 'next');
        hub.getDrillDown.mockResolvedValue(Object.assign(new DrillDownResponse(), {json: '{"cut":'}));
        button.dispatchEvent(new MouseEvent('mouseenter'));
        await settle();

        expect(document.querySelector('pre')?.textContent).toBe('{"cut":');
    });

    it('replaces stale content with errors and recovers on the next refresh', async () => {
        jest.useFakeTimers();
        hub.getDrillDown.mockReset()
            .mockResolvedValueOnce(Object.assign(new DrillDownResponse(), {errorMessage: 'Depth limit exceeded'}))
            .mockResolvedValueOnce(response('12'));
        const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
        button.dispatchEvent(new FocusEvent('focus'));
        await settle();

        expect(document.querySelector('[role="alert"]')?.textContent).toContain('Depth limit exceeded');
        expect(document.querySelector('[role="tooltip"]')?.textContent).not.toContain('Price');

        jest.advanceTimersByTime(5000);
        await settle();

        expect(document.querySelector('[role="alert"]')).toBeNull();
        expect(document.querySelector('[role="tooltip"]')?.textContent).toContain('12');
    });

    it('deduplicates focus and hover, fences pending work, and keeps the original request context', async () => {
        jest.useFakeTimers();
        let resolve!: (value: DrillDownResponse) => void;
        hub.getDrillDown.mockReset().mockReturnValueOnce(new Promise<DrillDownResponse>(done => resolve = done));
        const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
        button.dispatchEvent(new FocusEvent('focus'));
        button.dispatchEvent(new MouseEvent('mouseenter'));

        expect(hub.getDrillDown).toHaveBeenCalledTimes(1);
        expect(hub.getDrillDown).toHaveBeenCalledWith(expect.objectContaining({
            id: 'original', objectPaths: ['Outer', 'Trading|Orders||Order'], excludeEventViews: true
        }));

        jest.advanceTimersByTime(5000);
        expect(hub.getDrillDown).toHaveBeenCalledTimes(1);
        button.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape'}));
        resolve(response('stale'));
        await settle();
        jest.advanceTimersByTime(5000);

        expect(document.querySelector('[role="tooltip"]')).toBeNull();
        expect(hub.getDrillDown).toHaveBeenCalledTimes(1);
    });

    it('allows pointer transit and closes when its originating context changes', async () => {
        jest.useFakeTimers();
        let resolve!: (value: DrillDownResponse) => void;
        hub.getDrillDown.mockReset().mockReturnValueOnce(new Promise<DrillDownResponse>(done => resolve = done));
        const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
        button.dispatchEvent(new MouseEvent('mouseenter'));
        fixture.detectChanges();
        button.dispatchEvent(new MouseEvent('mouseleave'));
        jest.advanceTimersByTime(149);
        (document.querySelector('[role="tooltip"]') as HTMLElement).dispatchEvent(new MouseEvent('mouseenter'));
        jest.advanceTimersByTime(1);

        expect(document.querySelector('[role="tooltip"]')).not.toBeNull();
        fixture.componentRef.setInput('processId', 'other');
        fixture.detectChanges();
        resolve(response('stale'));
        await settle();

        expect(document.querySelector('[role="tooltip"]')).toBeNull();
        expect(hub.getDrillDown).toHaveBeenCalledWith(expect.objectContaining({id: 'original'}));
    });

    it('emits inspect and closes immediately when its trigger is clicked', async () => {
        const inspect = jest.fn();
        fixture.componentInstance.inspect.subscribe(inspect);
        const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
        button.dispatchEvent(new FocusEvent('focus'));
        await settle();
        button.click();
        fixture.detectChanges();

        expect(inspect).toHaveBeenCalledTimes(1);
        expect(document.querySelector('[role="tooltip"]')).toBeNull();
    });

    it('stops hover-only polling when CDK dismisses Escape from the body', async () => {
        jest.useFakeTimers();
        hub.getDrillDown.mockReset().mockResolvedValue(response('10'));
        const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
        button.dispatchEvent(new MouseEvent('mouseenter'));
        await settle();
        document.body.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape', bubbles: true}));
        await settle();
        jest.advanceTimersByTime(5300);

        expect(document.querySelector('[role="tooltip"]')).toBeNull();
        expect(hub.getDrillDown).toHaveBeenCalledTimes(1);
    });

    it('closes a reopened preview after an old hovered overlay was dismissed', async () => {
        jest.useFakeTimers();
        const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
        button.dispatchEvent(new MouseEvent('mouseenter'));
        await settle();
        (document.querySelector('[role="tooltip"]') as HTMLElement).dispatchEvent(new MouseEvent('mouseenter'));
        document.body.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape', bubbles: true}));
        await settle();
        button.dispatchEvent(new MouseEvent('mouseenter'));
        await settle();
        expect(document.querySelector('[role="tooltip"]')).not.toBeNull();
        button.dispatchEvent(new MouseEvent('mouseleave'));
        jest.advanceTimersByTime(150);
        fixture.detectChanges();
        jest.advanceTimersByTime(5300);

        expect(document.querySelector('[role="tooltip"]')).toBeNull();
        expect(hub.getDrillDown).toHaveBeenCalledTimes(2);
    });

    it('keeps a focused preview open after pointer departure and closes it after blur', async () => {
        jest.useFakeTimers();
        const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
        button.dispatchEvent(new FocusEvent('focus'));
        button.dispatchEvent(new MouseEvent('mouseenter'));
        fixture.detectChanges();
        button.dispatchEvent(new MouseEvent('mouseleave'));
        jest.advanceTimersByTime(150);
        expect(document.querySelector('[role="tooltip"]')).not.toBeNull();

        button.dispatchEvent(new FocusEvent('blur'));
        jest.advanceTimersByTime(150);
        fixture.detectChanges();
        expect(document.querySelector('[role="tooltip"]')).toBeNull();
    });

    it('closes and fences a preview when its property path mutates in place', async () => {
        jest.useFakeTimers();
        let path = 'Trading|Orders||Order';
        fixture.componentRef.setInput('prop', {name: 'Order', getPropertyPath: () => path});
        const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
        button.dispatchEvent(new MouseEvent('mouseenter'));
        await settle();
        path = 'Trading|Orders||Replacement';
        fixture.detectChanges();
        jest.advanceTimersByTime(5000);

        expect(document.querySelector('[role="tooltip"]')).toBeNull();
        expect(hub.getDrillDown).toHaveBeenCalledTimes(1);
    });

    it('renders empty results and diagnostic exceptions without stale properties', async () => {
        hub.getDrillDown.mockReset().mockResolvedValue(Object.assign(new DrillDownResponse(), {
            diagnostics: Object.assign(new DiagnosticResponse(), {exceptionMessage: 'Agent failed'})
        }));
        const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
        button.dispatchEvent(new FocusEvent('focus'));
        await settle();
        expect(document.querySelector('[role="alert"]')?.textContent).toContain('Agent failed');

        button.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape'}));
        fixture.componentRef.setInput('processId', 'empty');
        hub.getDrillDown.mockResolvedValue(new DrillDownResponse());
        button.dispatchEvent(new MouseEvent('mouseenter'));
        await settle();
        expect(document.querySelector('[role="tooltip"]')?.textContent).toContain('No properties');

        button.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape'}));
        fixture.componentRef.setInput('processId', 'empty-json');
        fixture.componentRef.setInput('json', true);
        button.dispatchEvent(new MouseEvent('mouseenter'));
        await settle();
        expect(document.querySelector('[role="tooltip"]')?.textContent).toContain('No properties');
        expect(document.querySelector('pre')).toBeNull();
    });

    it('keeps a reopened request pending when the old request resolves first', async () => {
        jest.useFakeTimers();
        let resolveOld!: (value: DrillDownResponse) => void;
        let resolveNew!: (value: DrillDownResponse) => void;
        hub.getDrillDown.mockReset()
            .mockReturnValueOnce(new Promise<DrillDownResponse>(done => resolveOld = done))
            .mockReturnValueOnce(new Promise<DrillDownResponse>(done => resolveNew = done));
        const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
        button.dispatchEvent(new MouseEvent('mouseenter'));
        button.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape'}));
        button.dispatchEvent(new MouseEvent('mouseenter'));
        resolveOld(response('old'));
        await settle();

        expect(document.querySelector('[role="tooltip"]')?.textContent).toContain('Loading preview…');
        jest.advanceTimersByTime(5000);
        expect(hub.getDrillDown).toHaveBeenCalledTimes(2);
        resolveNew(response('new'));
        await settle();
        expect(document.querySelector('[role="tooltip"]')?.textContent).toContain('new');
    });

    it('keeps new preview data when the new request resolves before the old request', async () => {
        let resolveOld!: (value: DrillDownResponse) => void;
        let resolveNew!: (value: DrillDownResponse) => void;
        hub.getDrillDown.mockReset()
            .mockReturnValueOnce(new Promise<DrillDownResponse>(done => resolveOld = done))
            .mockReturnValueOnce(new Promise<DrillDownResponse>(done => resolveNew = done));
        const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
        button.dispatchEvent(new MouseEvent('mouseenter'));
        button.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape'}));
        button.dispatchEvent(new MouseEvent('mouseenter'));
        resolveNew(response('new'));
        await settle();
        resolveOld(response('old'));
        await settle();

        expect(document.querySelector('[role="tooltip"]')?.textContent).toContain('new');
        expect(document.querySelector('[role="tooltip"]')?.textContent).not.toContain('old');
    });

    it('stops polling when destroyed', async () => {
        jest.useFakeTimers();
        const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
        button.dispatchEvent(new MouseEvent('mouseenter'));
        await settle();
        fixture.destroy();
        jest.advanceTimersByTime(5000);

        expect(hub.getDrillDown).toHaveBeenCalledTimes(1);
    });
});
