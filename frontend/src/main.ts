import { bootstrapApplication } from '@angular/platform-browser';
import { LOCALE_ID } from '@angular/core';
import { DATE_PIPE_DEFAULT_OPTIONS, registerLocaleData } from '@angular/common';
import pt from '@angular/common/locales/pt';
import { AppComponent } from './app.component';
registerLocaleData(pt);
bootstrapApplication(AppComponent,{providers:[{provide:LOCALE_ID,useValue:'pt-BR'},{provide:DATE_PIPE_DEFAULT_OPTIONS,useValue:{timezone:'-0300'}}]}).catch(console.error);
