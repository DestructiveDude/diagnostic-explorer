import {NgModule} from '@angular/core';
import {BrowserModule} from '@angular/platform-browser';

import {AppRoutingModule} from './app-routing.module';
import {AppComponent} from './app.component';
import {BrowserAnimationsModule} from "@angular/platform-browser/animations";
import {RetroNavComponent} from './retro-nav/retro-nav.component';
import {RetroDisplayComponent} from './retro-display/retro-display.component';
import {RealtimeNavComponent} from './realtime-nav/realtime-nav.component';
import {RealtimeDisplayComponent} from './realtime-display/realtime-display.component';
import { provideHttpClient, withInterceptorsFromDi, withXhr } from '@angular/common/http';
import {FormsModule} from '@angular/forms';
import {RealtimeCategoryComponent} from './realtime-category/realtime-category.component';
import {RealtimeEventsComponent} from './realtime-events/realtime-events.component';
import {EventFilterComponent} from './event-filter/event-filter.component';
import {SetPropertyDialogComponent} from './set-property-dialog/set-property-dialog.component';
import {DynamicDialogModule, DialogService} from 'primeng/dynamicdialog';
import {InfoDialogComponent} from './info-dialog/info-dialog.component';
import {ExecOperationsComponent} from './exec-operations/exec-operations.component';
import {APP_BASE_HREF, DatePipe} from '@angular/common';
import {SummaryLinePipe} from './pipes/summary-line.pipe';
import {LevelNamePipe} from './pipes/level-name.pipe';
import {CategoryNavComponent} from './category-nav/category-nav.component';
import {TraceScopeComponent} from './trace-scope/trace-scope.component';
import {EventDetailComponent} from './event-detail/event-detail.component';
import {getBaseLocation} from "./util/util";
import {BASE_API_URL, BASE_API_KEY} from "../injectionTokens";
import {environment} from "../environments/environment";
import {providePrimeNG} from 'primeng/config';
import Aura from '@primeng/themes/aura';
import {definePreset} from '@primeng/themes';

// Rebrand Aura's default (teal/emerald) primary to the app's salmon accent (#fd8c73)
// so PrimeNG highlight states — select selected-option, datepicker selected date —
// match the rest of the UI instead of showing teal.
const SalmonAura = definePreset(Aura, {
    semantic: {
        primary: {
            50: '#fff4f1', 100: '#ffe4dc', 200: '#ffc9b9', 300: '#ffa893',
            400: '#fd9379', 500: '#fd8c73', 600: '#e07060', 700: '#bd5a4d',
            800: '#9a4940', 900: '#7e3e37', 950: '#451d19'
        }
    }
});
import {TableModule} from 'primeng/table';
import {CheckboxModule} from 'primeng/checkbox';
import {InputTextModule} from 'primeng/inputtext';
import {IconFieldModule} from 'primeng/iconfield';
import {InputIconModule} from 'primeng/inputicon';
import {ContextMenuModule} from 'primeng/contextmenu';
import {PanelModule} from 'primeng/panel';
import {TooltipModule} from 'primeng/tooltip';
import {TabsModule} from 'primeng/tabs';
import {ButtonModule} from 'primeng/button';
import {FieldsetModule} from 'primeng/fieldset';
import {SplitterModule} from 'primeng/splitter';
import {SelectButtonModule} from 'primeng/selectbutton';
import {SelectModule} from 'primeng/select';
import {DatePickerModule} from 'primeng/datepicker';
import {ProgressBarModule} from 'primeng/progressbar';
import {ToastModule} from 'primeng/toast';
import {MessageService} from 'primeng/api';

@NgModule({ declarations: [
        AppComponent,
        RetroNavComponent,
        RetroDisplayComponent,
        RealtimeNavComponent,
        RealtimeDisplayComponent,
        RealtimeCategoryComponent,
        RealtimeEventsComponent,
        EventFilterComponent,
        SetPropertyDialogComponent,
        InfoDialogComponent,
        ExecOperationsComponent,
        SummaryLinePipe,
        LevelNamePipe,
        CategoryNavComponent,
        TraceScopeComponent,
        EventDetailComponent
    ],
    bootstrap: [AppComponent], imports: [BrowserModule,
        AppRoutingModule,
        DynamicDialogModule,
        BrowserAnimationsModule,
        FormsModule,
        TableModule,
        CheckboxModule,
        InputTextModule,
        IconFieldModule,
        InputIconModule,
        ContextMenuModule,
        PanelModule,
        TooltipModule,
        TabsModule,
        ButtonModule,
        FieldsetModule,
        SplitterModule,
        SelectButtonModule,
        SelectModule,
        DatePickerModule,
        ProgressBarModule,
        ToastModule], providers: [
        { provide: APP_BASE_HREF, useFactory: getBaseLocation },
        { provide: BASE_API_URL, useValue: environment.apiRoot },
        { provide: BASE_API_KEY, useValue: environment.apiKey },
        DatePipe,
        DialogService,
        MessageService,
        provideHttpClient(withXhr(), withInterceptorsFromDi()),
        providePrimeNG({ theme: { preset: SalmonAura, options: { darkModeSelector: '.app-dark' } } })
    ] })
export class AppModule {
}
