import {Component, OnInit} from '@angular/core';
import {RetroModel} from '../Model/RetroModel';

@Component({
    selector: 'app-retro-nav',
    templateUrl: './retro-nav.component.html',
    styleUrls: ['./retro-nav.component.scss'],
    standalone: false
})
export class RetroNavComponent implements OnInit {


    constructor(readonly model: RetroModel) {
    }

    times = Array.from(Array(24).keys());
    hours = [
        {hours: 1, text: "1 Hour"},
        {hours: 2, text: "2 Hours"},
        {hours: 4, text: "4 Hours"},
        {hours: 8, text: "8 Hours"},
        {hours: 12, text: "12 Hours"},
        {hours: 24, text: "1 Day"},
        {hours: 2 * 24, text: "2 Days"},
        {hours: 5 * 24, text: "5 Days"},
        {hours: 7 * 24, text: "7 Days"},
        {hours: 14 * 24, text: "14 Days"},
        {hours: 30 * 24, text: "30 Days"},
    ];

    maxRecordOptions = [
        { label: '1,000', value: 1000 },
        { label: '5,000', value: 5000 },
        { label: '10,000', value: 10000 },
        { label: '20,000', value: 20000 },
    ];
    minLevelOptions = [
        { label: 'All', value: 0 },
        { label: 'Verbose', value: 10000 },
        { label: 'Trace', value: 20000 },
        { label: 'Debug', value: 30000 },
        { label: 'Info', value: 40000 },
        { label: 'Notice', value: 50000 },
        { label: 'Warn', value: 60000 },
        { label: 'Error', value: 70000 },
        { label: 'Severe', value: 80000 },
        { label: 'Critical', value: 90000 },
        { label: 'Alert', value: 100000 },
        { label: 'Fatal', value: 110000 },
        { label: 'Emergency', value: 120000 },
    ];
    timeOptions = this.times.map(h => ({ label: `${h.toString().padStart(2, '0')}:00`, value: h }));
    hourOptions = this.hours.map(h => ({ label: h.text, value: h.hours }));

    ngOnInit(): void {
    }

}
