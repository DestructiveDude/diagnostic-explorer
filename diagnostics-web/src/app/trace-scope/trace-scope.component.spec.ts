import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TraceScopeComponent } from './trace-scope.component';
import { ScopeNode } from '../Model/ScopeNode';

const RAW = `[00.000] [00.042] BEGIN ProcessOrder
  [00.001] [00.003] BEGIN Validate
    CheckLimits: ok
  [00.004] [00.003] END Validate
[00.042] [00.042] END ProcessOrder`;

describe('TraceScopeComponent', () => {
  let fixture: ComponentFixture<TraceScopeComponent>;
  beforeEach(async () => {
    await TestBed.configureTestingModule({ declarations: [TraceScopeComponent] }).compileComponents();
    fixture = TestBed.createComponent(TraceScopeComponent);
  });

  it('renders a summary per BEGIN region and nests children', () => {
    fixture.componentInstance.node = ScopeNode.parseTraceScope(RAW)!;
    fixture.detectChanges();
    const summaries = fixture.nativeElement.querySelectorAll('summary.scope');
    expect(summaries.length).toBe(2);                 // ProcessOrder + Validate
    expect(fixture.nativeElement.textContent).toContain('ProcessOrder');
    expect(fixture.nativeElement.textContent).toContain('Validate');
  });

  it('shows leaf lines under their scope', () => {
    fixture.componentInstance.node = ScopeNode.parseTraceScope(RAW)!;
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.leaf')?.textContent).toContain('CheckLimits');
  });
});
