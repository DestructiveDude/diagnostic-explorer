import { Component, Input, ChangeDetectionStrategy } from '@angular/core';
import { ScopeNode } from '../Model/ScopeNode';

@Component({
  selector: 'app-trace-scope',
  templateUrl: './trace-scope.component.html',
  styleUrls: ['./trace-scope.component.scss'],
  changeDetection: ChangeDetectionStrategy.Eager,
  standalone: false,
})
export class TraceScopeComponent {
  @Input() node?: ScopeNode;

  // First line is "[ss.mmm] [ss.mmm] BEGIN Label": the first bracket is the scope's
  // start offset (seconds.milliseconds), the second is the gap since the previous line.
  // The actual scope duration is appended at the end of the label in the form "Label (N.NNN seconds)".
  // We parse the suffix if present to get the true duration in milliseconds; otherwise we fall back
  // to the gap timespan.
  parse(firstLine: string): { start: string; dur: number; label: string } {
    const m = /^\[(\d{2})\.(\d{3})\]\s*\[(\d{2})\.(\d{3})\]\s*BEGIN\s*(.*)$/.exec(firstLine ?? '');
    if (!m) return { start: '', dur: 0, label: firstLine ?? '' };
    const start = `${parseInt(m[1], 10)}.${m[2]}`;
    let dur = parseInt(m[3], 10) * 1000 + parseInt(m[4], 10);
    let label = m[5].trim();
    const durMatch = /\(([\d.]+)\s*seconds\)$/.exec(label);
    if (durMatch) {
      dur = Math.round(parseFloat(durMatch[1]) * 1000);
      label = label.replace(/\s*\([\d.]+\s*seconds\)$/, '').trim();
    }
    return { start, dur, label };
  }

  isBig(dur: number): boolean { return dur >= 20; }

  // <details> toggles its own `open` state; mirror it back onto the node so the
  // expanded/collapsed choice survives change detection and re-renders.
  onToggle(node: ScopeNode, event: Event): void {
    node.expanded = (event.target as HTMLDetailsElement).open;
  }
}
