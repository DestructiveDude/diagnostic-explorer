import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CategoryModel } from '../Model/CategoryModel';

@Component({
  selector: 'app-category-nav',
  templateUrl: './category-nav.component.html',
  styleUrls: ['./category-nav.component.scss'],
  standalone: false,
})
export class CategoryNavComponent {
  @Input() categories: CategoryModel[] = [];
  @Input() selectedIndex = 0;
  @Output() selectedIndexChange = new EventEmitter<number>();

  select(i: number) { this.selectedIndexChange.emit(i); }
}
