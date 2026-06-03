import { Component, Input } from '@angular/core';
import { ScopeNode } from '../Model/ScopeNode';

@Component({
  selector: 'app-trace-scope',
  templateUrl: './trace-scope.component.html',
  styleUrls: ['./trace-scope.component.scss'],
  standalone: false,
})
export class TraceScopeComponent {
  @Input() node?: ScopeNode;

  // First line is "[ss.mmm] [ss.mmm] BEGIN Label": the first bracket is the scope's
  // start offset (seconds.milliseconds), the second is its own duration in the same
  // seconds.milliseconds form. So "[00.005] [00.028] BEGIN Persist" -> start '0.005',
  // dur 28 (ms); "[01.005] ... BEGIN X" -> dur 1005 (ms). Hence dur = seconds*1000 + ms.
  parse(firstLine: string): { start: string; dur: number; label: string } {
    const m = /^\[(\d{2})\.(\d{3})\]\s*\[(\d{2})\.(\d{3})\]\s*BEGIN\s*(.*)$/.exec(firstLine ?? '');
    if (!m) return { start: '', dur: 0, label: firstLine ?? '' };
    const start = `${parseInt(m[1], 10)}.${m[2]}`;
    const dur = parseInt(m[3], 10) * 1000 + parseInt(m[4], 10);
    return { start, dur, label: m[5].trim() };
  }

  isBig(dur: number): boolean { return dur >= 20; }

  // <details> toggles its own `open` state; mirror it back onto the node so the
  // expanded/collapsed choice survives change detection and re-renders.
  onToggle(node: ScopeNode, event: Event): void {
    node.expanded = (event.target as HTMLDetailsElement).open;
  }
}
