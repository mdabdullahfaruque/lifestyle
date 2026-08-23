import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Util } from './util';

describe('Util', () => {
  let component: Util;
  let fixture: ComponentFixture<Util>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Util]
    })
    .compileComponents();

    fixture = TestBed.createComponent(Util);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
