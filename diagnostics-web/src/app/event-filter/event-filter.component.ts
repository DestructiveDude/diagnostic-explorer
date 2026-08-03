import {Component, EventEmitter, Input, OnInit, Output, ChangeDetectionStrategy} from '@angular/core';
import {FilterCriteria} from '../Model/FilterCriteria';
import {Watch} from '../util/Watch';

@Component({
    selector: 'app-event-filter',
    templateUrl: './event-filter.component.html',
    styleUrls: ['./event-filter.component.scss'],
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class EventFilterComponent implements OnInit {


    watchEnabled = false;

    constructor() {
        this.watchEnabled = true;
    }

    ngOnInit(): void {
    }

    @Input()
    @Watch((_this: EventFilterComponent) => _this.filterVisibleChange.emit(_this.filterVisible))
    filterVisible = true;

    @Output()
    filterVisibleChange = new EventEmitter<boolean>();

    private _criteria = new FilterCriteria();

    @Input()
    @Watch((_this: EventFilterComponent) => _this.loadCriteria())
    get criteria(): FilterCriteria {
        return this._criteria;
    }

    set criteria(value: FilterCriteria) {
        this._criteria = value;
    }

    @Output()
    criteriaChange = new EventEmitter<FilterCriteria>();

    @Watch((_this: EventFilterComponent) => _this.onCriteriaChanged())
    searchText = '';

    @Watch((_this: EventFilterComponent) => _this.onCriteriaChanged())
    info = false;

    @Watch((_this: EventFilterComponent) => _this.onCriteriaChanged())
    notice = false;

    @Watch((_this: EventFilterComponent) => _this.onCriteriaChanged())
    warn = false;

    @Watch((_this: EventFilterComponent) => _this.onCriteriaChanged())
    error = false;

    @Watch((_this: EventFilterComponent) => _this.onCriteriaChanged())
    hasScope = false;


    private onCriteriaChanged(): void {
        const criteria = new FilterCriteria();
        criteria.searchText = this.searchText;
        criteria.info = this.info;
        criteria.notice = this.notice;
        criteria.warn = this.warn;
        criteria.error = this.error;
        criteria.hasScope = this.hasScope;
        this._criteria = criteria;
        this.criteriaChange.emit(criteria);
    }

    private loadCriteria(): void {
        // Reflect an inbound `criteria` binding into the individual fields.
        // Suppress the @Watch callbacks while doing so: otherwise the first
        // assignment (searchText) re-enters via its own @Watch and runs
        // onCriteriaChanged(), which rebuilds _criteria from the still-default
        // flag fields and clobbers it before the level flags are copied across
        // — losing everything but searchText. Suppressing also stops this
        // inbound load from echoing back out as criteriaChange events.
        const criteria = this._criteria;
        const wasWatching = this.watchEnabled;
        this.watchEnabled = false;
        try {
            this.searchText = criteria.searchText;
            this.info = criteria.info;
            this.notice = criteria.notice;
            this.warn = criteria.warn;
            this.error = criteria.error;
            this.hasScope = criteria.hasScope;
        } finally {
            this.watchEnabled = wasWatching;
        }
    }
}
