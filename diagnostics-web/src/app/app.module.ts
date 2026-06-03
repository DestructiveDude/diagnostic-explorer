import {NgModule} from '@angular/core';
import {BrowserModule} from '@angular/platform-browser';

import {AppRoutingModule} from './app-routing.module';
import {AppComponent} from './app.component';
import {MatTabsModule} from "@angular/material/tabs";
import {MatSidenavModule} from "@angular/material/sidenav";
import {BrowserAnimationsModule} from "@angular/platform-browser/animations";
import {MatToolbarModule} from "@angular/material/toolbar";
import {RetroNavComponent} from './retro-nav/retro-nav.component';
import {RetroDisplayComponent} from './retro-display/retro-display.component';
import {RealtimeNavComponent} from './realtime-nav/realtime-nav.component';
import {RealtimeDisplayComponent} from './realtime-display/realtime-display.component';
import {MatIconModule} from "@angular/material/icon";
import {MatButtonModule} from "@angular/material/button";
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import {MatTableModule} from '@angular/material/table';
import {MatInputModule} from '@angular/material/input';
import {MatListModule} from '@angular/material/list';
import {FormsModule} from '@angular/forms';
import {RealtimeCategoryComponent} from './realtime-category/realtime-category.component';
import {MatExpansionModule} from '@angular/material/expansion';
import {MatCardModule} from '@angular/material/card';
import {RealtimeEventsComponent} from './realtime-events/realtime-events.component';
import {MatTooltipModule} from '@angular/material/tooltip';
import {MatSnackBarModule} from '@angular/material/snack-bar';
import {EventFilterComponent} from './event-filter/event-filter.component';
import {MatCheckboxModule} from '@angular/material/checkbox';
import {SetPropertyDialogComponent} from './set-property-dialog/set-property-dialog.component';
import {MatDialogModule} from '@angular/material/dialog';
import {InfoDialogComponent} from './info-dialog/info-dialog.component';
import {ExecOperationsComponent} from './exec-operations/exec-operations.component';
import {MatMenuModule} from '@angular/material/menu';
import {MatSelectModule} from '@angular/material/select';
import {MatDatepickerModule} from '@angular/material/datepicker';
import {MAT_DATE_LOCALE, MatNativeDateModule} from '@angular/material/core';
import {APP_BASE_HREF, DatePipe} from '@angular/common';
import {SummaryLinePipe} from './pipes/summary-line.pipe';
import {MatProgressBarModule} from '@angular/material/progress-bar';
import {LevelNamePipe} from './pipes/level-name.pipe';
import {AngularSplitModule} from 'angular-split';
import {CategoryNavComponent} from './category-nav/category-nav.component';
import {TraceScopeComponent} from './trace-scope/trace-scope.component';
import {EventDetailComponent} from './event-detail/event-detail.component';
import {getBaseLocation} from "./util/util";
import {BASE_API_URL, BASE_API_KEY} from "../injectionTokens";
import {environment} from "../environments/environment";
import {providePrimeNG} from 'primeng/config';
import Aura from '@primeng/themes/aura';
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
        MatDialogModule,
        MatTabsModule,
        MatSidenavModule,
        BrowserAnimationsModule,
        MatToolbarModule,
        MatIconModule,
        MatButtonModule,
        MatSnackBarModule,
        MatTableModule,
        MatInputModule,
        FormsModule,
        MatExpansionModule,
        MatCardModule,
        MatTooltipModule,
        MatCheckboxModule,
        MatMenuModule,
        MatSelectModule,
        MatDatepickerModule,
        MatNativeDateModule,
        MatProgressBarModule,
        AngularSplitModule,
        MatListModule,
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
        ProgressBarModule], providers: [
        { provide: MAT_DATE_LOCALE, useValue: 'en-GB' },
        { provide: APP_BASE_HREF, useFactory: getBaseLocation },
        { provide: BASE_API_URL, useValue: environment.apiRoot },
        { provide: BASE_API_KEY, useValue: environment.apiKey },
        DatePipe,
        provideHttpClient(withInterceptorsFromDi()),
        providePrimeNG({ theme: { preset: Aura, options: { darkModeSelector: '.app-dark' } } })
    ] })
export class AppModule {
}
