import { Component, EventEmitter, Input, Output } from '@angular/core';
import { EventModel } from '../Model/EventModel';
import { ScopeNode } from '../Model/ScopeNode';

const DUMMY_TRACE = `
[00.000] [00.087] BEGIN HandleRequest POST /api/diagnostics/events
[00.001] [00.014] BEGIN AuthoriseRequest
[00.001] [00.009] BEGIN ValidateToken jwt-bearer
[00.001] [00.008] END ValidateToken jwt-bearer
[00.010] [00.005] END AuthoriseRequest
[00.015] [00.065] BEGIN ProcessDiagnostic WorkerA::EventSink
[00.015] [00.008] BEGIN LoadProcess WorkerA
[00.015] [00.007] END LoadProcess WorkerA
[00.023] [00.031] BEGIN QueryEvents
[00.023] [00.028] BEGIN BuildQuery mongo
[00.024] [00.003] BEGIN ApplyFilters level=warn,machine=SRV01
[00.024] [00.002] END ApplyFilters level=warn,machine=SRV01
[00.027] [00.022] END BuildQuery mongo
[00.049] [00.005] END QueryEvents
[00.054] [00.022] BEGIN PersistResults
[00.054] [00.019] BEGIN WriteEvents 41 records
[00.054] [00.018] END WriteEvents 41 records
[00.073] [00.003] END PersistResults
[00.076] [00.004] END ProcessDiagnostic WorkerA::EventSink
[00.080] [00.007] END HandleRequest POST /api/diagnostics/events
`.trim();

@Component({
  selector: 'app-event-detail',
  templateUrl: './event-detail.component.html',
  styleUrls: ['./event-detail.component.scss'],
  standalone: false,
})
export class EventDetailComponent {
  @Input() event?: EventModel;
  @Output() closed = new EventEmitter<void>();

  readonly dummyScope: ScopeNode = ScopeNode.parseTraceScope(DUMMY_TRACE)!;
}
