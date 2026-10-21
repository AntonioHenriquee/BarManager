import { Component, OnDestroy, OnInit, signal, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Api } from './api.service';
type Field={key:string;label:string;type?:string;required?:boolean;options?:{value:any;label:string}[];step?:string;min?:number;max?:number};
@Component({selector:'app-root',standalone:true,imports:[CommonModule,FormsModule],templateUrl:'./app.component.html'})
export class AppComponent implements OnInit,OnDestroy {
 api=inject(Api);user=signal<any>(null);checking=signal(true);page=signal('dashboard');error=signal('');notice=signal('');busy=signal(false);mobile=signal(false);
 login={login:'admin',password:''};data=signal<any>({});tables=signal<any[]>([]);products=signal<any[]>([]);customers=signal<any[]>([]);tabs=signal<any[]>([]);sync=signal<any>({});selected=signal<any>(null);search=signal('');
 modal=signal('');fields:Field[]=[];model:any={};savePath='';saveMethod='POST';afterSave:(()=>Promise<void>)|null=null;
 selectedProduct='';quantity=1;addingOperation='';payments:{method:string;amount:number}[]=[];creditAmount=0;discount=0;customerId='';dueAt='';closeOperation='';
 from=new Date(new Date().setDate(1)).toISOString().slice(0,10);to=new Date().toISOString().slice(0,10);grouping='day';statement=signal<any>(null);
 methods=[{value:'Cash',label:'Dinheiro'},{value:'Pix',label:'Pix'},{value:'Debit',label:'Cartão de débito'},{value:'Credit',label:'Cartão de crédito'},{value:'Other',label:'Outros'}];
 nav=[{id:'dashboard',label:'Dashboard',icon:'◫',admin:true},{id:'tables',label:'Mesas e Balcão',icon:'▦'},{id:'sale',label:'Nova Venda',icon:'＋'},{id:'customers',label:'Clientes',icon:'♧',admin:true},{id:'credit',label:'Fiados',icon:'◷',receive:true},{id:'payments',label:'Recebimentos',icon:'↓',receive:true},{id:'products',label:'Produtos',icon:'▣'},{id:'stock',label:'Estoque',icon:'▤',admin:true},{id:'cash',label:'Caixa',icon:'▢',receive:true},{id:'expenses',label:'Despesas',icon:'↗',admin:true},{id:'reports',label:'Relatórios',icon:'▥',admin:true},{id:'users',label:'Funcionários',icon:'♙',admin:true},{id:'sync',label:'Sincronização e Backup',icon:'⟳',admin:true},{id:'settings',label:'Configurações',icon:'⚙',admin:true}];
 visibleNav=computed(()=>this.nav.filter(n=>(!n.admin||this.admin())&&(!n.receive||this.user()?.canReceive||(n.id==='cash'&&this.user()?.canCloseCash))));
 title=computed(()=>this.nav.find(n=>n.id===this.page())?.label||'Meu Bar');
 filteredProducts=computed(()=>this.products().filter(p=>`${p.name} ${p.code} ${p.category}`.toLowerCase().includes(this.search().toLowerCase())));
 filteredCustomers=computed(()=>this.customers().filter(c=>`${c.name} ${c.nickname} ${c.phone}`.toLowerCase().includes(this.search().toLowerCase())));
 availableProducts=computed(()=>this.products().filter(p=>p.active));
 admin(){return this.user()?.role==='Admin';}
 timer:any;
 async ngOnInit(){try{this.user.set(await this.api.get('/auth/me'));await this.navigate(this.admin()?'dashboard':'tables');}catch{}finally{this.checking.set(false);}this.timer=setInterval(()=>{if(this.user()&&!this.busy())this.refreshLive();},5000);}
 ngOnDestroy(){clearInterval(this.timer);}
 async run(action:()=>Promise<void>){if(this.busy())return;this.busy.set(true);this.error.set('');try{await action();}catch(e:any){this.error.set(e.message||'Não foi possível concluir.');}finally{this.busy.set(false);}}
 async signIn(){await this.run(async()=>{await this.api.post('/auth/login',this.login);this.login.password='';this.user.set(await this.api.get('/auth/me'));await this.navigate(this.admin()?'dashboard':'tables');});}
 async signOut(){await this.run(async()=>{await this.api.post('/auth/logout',{});this.user.set(null);this.selected.set(null);this.modal.set('');this.data.set({});});}
 liveRefreshing=false;
 async refreshLive(){
  if(this.liveRefreshing||this.busy()||this.modal()||!this.user())return;
  this.liveRefreshing=true;
  try{
   await this.refreshStatus();
   const id=this.selected()?.tab.id;
   if(id){const detail=await this.api.get('/tabs/'+id);if(this.selected()?.tab.id===id&&!this.busy()&&!this.modal()){
    if(detail.tab.status==='Closed'||detail.tab.status==='Cancelled'||detail.tab.status==='Merged'){this.selected.set(null);this.notice.set('Esta comanda foi encerrada em outro atendimento. O histórico foi preservado.');await this.load();}
    else this.selected.set(detail);
   }}else if(['tables','sale','dashboard'].includes(this.page()))await this.load();
  }catch{/* A próxima consulta tenta novamente; o banner mantém a última proteção confirmada. */}
  finally{this.liveRefreshing=false;}
 }
 async refreshStatus(){try{this.sync.set(await this.api.get('/sync/status'));}catch{this.sync.update(v=>({...v,connected:false}));}}
 async navigate(page:string){this.page.set(page);this.search.set('');this.mobile.set(false);this.selected.set(null);this.statement.set(null);await this.runLoad();}
 async runLoad(){try{await this.load();await this.refreshStatus();}catch(e:any){this.error.set(e.message);}}
 async load(){switch(this.page()){
 case 'dashboard':this.data.set(await this.api.get('/dashboard'));break;
 case 'tables':case 'sale':this.tables.set(await this.api.get('/tables'));this.tabs.set(await this.api.get('/tabs'));this.products.set(await this.api.get('/products'));this.customers.set(await this.api.get('/customers'));break;
 case 'products':case 'stock':this.products.set(await this.api.get('/products'));if(this.page()==='stock')this.data.set({movements:await this.api.get('/stock')});break;
 case 'customers':this.customers.set(await this.api.get('/customers'));break;
 case 'credit':this.data.set({accounts:await this.api.get('/receivables')});break;
 case 'payments':this.data.set({payments:await this.api.get('/payments')});break;
 case 'cash':case 'expenses':this.data.set(await this.api.get('/cash'));break;
 case 'users':this.data.set({users:await this.api.get('/users')});break;
 case 'reports':this.data.set(await this.api.get(`/reports?from=${this.from}&to=${this.to}`));break;
 case 'sync':this.data.set({audit:await this.api.get('/audit')});break;
 }}
 openForm(title:string,path:string,fields:Field[],values:any={},method='POST',after:(()=>Promise<void>)|null=null){this.error.set('');this.model={operationId:crypto.randomUUID(),...values};this.fields=fields;this.savePath=path;this.saveMethod=method;this.afterSave=after;this.modal.set(title);}
 async submit(){await this.run(async()=>{const result=await this.api.request(this.savePath,this.saveMethod,this.model);this.model.result=result;this.modal.set('');this.notice.set('Operação registrada com sucesso.');if(this.afterSave)await this.afterSave();else await this.load();await this.refreshStatus();});}
 field(key:string,label:string,type='text',required=true):Field{return{key,label,type,required};}
 moneyField(key:string,label:string):Field{return{key,label,type:'number',required:true,step:'0.01',min:0,max:999999999};}
 productForm(p:any=null){this.openForm(p?'Editar produto':'Cadastrar produto',p?`/products/${p.id}`:'/products',[this.field('code','Código'),this.field('name','Nome'),this.field('category','Categoria','text',false),this.field('unit','Unidade'),this.field('barcode','Código de barras','text',false),this.field('supplier','Fornecedor','text',false),this.moneyField('cost','Custo (R$)'),this.moneyField('price','Preço de venda (R$)'),{...this.moneyField('minimumStock','Estoque mínimo'),step:'0.001'},this.field('active','Produto ativo','checkbox',false)],p||{code:'',name:'',unit:'un',category:'',barcode:'',supplier:'',cost:0,price:0,minimumStock:0,active:true},p?'PUT':'POST');}
 customerForm(c:any=null){this.openForm(c?'Editar cliente':'Cadastrar cliente',c?`/customers/${c.id}`:'/customers',[this.field('name','Nome'),this.field('nickname','Apelido','text',false),this.field('phone','Telefone','tel',false),this.field('whatsApp','WhatsApp','tel',false),this.field('address','Endereço','text',false),this.field('notes','Observações','textarea',false),this.moneyField('creditLimit','Limite de crédito (R$)'),this.field('creditAuthorized','Autorizar compras fiadas','checkbox',false),this.field('active','Cliente ativo','checkbox',false)],c||{name:'',nickname:'',phone:'',whatsApp:'',address:'',notes:'',creditLimit:0,creditAuthorized:false,active:true},c?'PUT':'POST');}
 userForm(u:any=null){this.openForm(u?'Editar funcionário':'Cadastrar funcionário',u?`/users/${u.id}`:'/users',[this.field('name','Nome'),this.field('login','Login'),{...this.field('password',u?'Nova senha (opcional)':'Senha (mínimo de 12 caracteres)','password',!u),min:12},{key:'role',label:'Perfil',type:'select',options:[{value:'Employee',label:'Funcionário'},{value:'Admin',label:'Administrador'}]},this.field('canReceive','Permitir recebimentos','checkbox',false),this.field('canCloseCash','Permitir fechamento de caixa','checkbox',false),this.field('active','Usuário ativo','checkbox',false)],u?{...u,password:''}:{name:'',login:'',password:'',role:'Employee',canReceive:false,canCloseCash:false,active:true},u?'PUT':'POST');}
 tableForm(){this.openForm('Cadastrar mesa ou balcão','/tables',[this.field('name','Identificação (ex.: Mesa 01)'),this.field('description','Descrição','text',false)],{name:'',description:''});}
 openTab(table:any=null){this.openForm(table?`Abrir ${table.name}`:'Venda rápida · nova comanda','/tabs',[this.field('consumer','Nome do consumidor (opcional)','text',false),this.field('notes','Observações','textarea',false)],{tableId:table?.id||null,consumer:'',notes:''},'POST',async()=>{await this.load();await this.loadSelection(this.model.result.id);});}
 async selectTab(id:string){await this.run(()=>this.loadSelection(id));}
 async loadSelection(id:string){this.selected.set(await this.api.get('/tabs/'+id));this.selectedProduct='';this.quantity=1;this.addingOperation=crypto.randomUUID();this.closeOperation=crypto.randomUUID();this.payments=[];this.creditAmount=0;this.discount=0;this.customerId='';this.dueAt='';}
 async addItem(){if(!this.selectedProduct){this.error.set('Selecione um produto.');return;}await this.run(async()=>{await this.api.post(`/tabs/${this.selected().tab.id}/items`,{operationId:this.addingOperation,productId:this.selectedProduct,quantity:Number(this.quantity)});this.selected.set(await this.api.get('/tabs/'+this.selected().tab.id));this.products.set(await this.api.get('/products'));this.addingOperation=crypto.randomUUID();this.quantity=1;await this.refreshStatus();});}
 async requestClose(){await this.run(async()=>{await this.api.post(`/tabs/${this.selected().tab.id}/request-close`,{operationId:crypto.randomUUID()});this.selected.set(await this.api.get('/tabs/'+this.selected().tab.id));});}
 closeForm(){this.payments=[{method:'Cash',amount:this.selected().total}];this.creditAmount=0;this.discount=0;this.modal.set('Fechar comanda');this.closeOperation=crypto.randomUUID();}
 async closeTab(){await this.run(async()=>{await this.api.post(`/tabs/${this.selected().tab.id}/close`,{operationId:this.closeOperation,payments:this.payments.map(p=>({method:p.method,amount:Number(p.amount)})),creditAmount:Number(this.creditAmount),discount:Number(this.discount),customerId:this.customerId||null,dueAt:this.dueAt?new Date(this.dueAt+'T23:59:59-03:00').toISOString():null});this.modal.set('');this.selected.set(null);await this.load();await this.refreshStatus();this.notice.set('Venda registrada. Mesa liberada e histórico preservado.');});}
 outstanding(){return Math.round((this.selected()?.total-Number(this.discount)-Number(this.creditAmount)-this.payments.reduce((s,p)=>s+Number(p.amount),0))*100)/100;}
 cancelItem(i:any){this.openForm('Cancelar item com devolução ao estoque',`/items/${i.id}/cancel`,[this.field('reason','Motivo','textarea')],{reason:''},'POST',async()=>{this.selected.set(await this.api.get('/tabs/'+this.selected().tab.id));});}
 cancelTab(){this.openForm('Cancelar comanda',`/tabs/${this.selected().tab.id}/cancel`,[this.field('reason','Motivo do cancelamento','textarea')],{reason:''},'POST',async()=>{this.selected.set(null);await this.load();});}
 transfer(){this.openForm('Transferir comanda',`/tabs/${this.selected().tab.id}/transfer`,[{key:'tableId',label:'Mesa livre de destino',type:'select',required:true,options:this.tables().filter(t=>t.status==='Free').map(t=>({value:t.id,label:t.name}))}],{},'POST',async()=>{this.selected.set(null);await this.load();});}
 merge(){this.openForm('Juntar outra comanda nesta',`/tabs/${this.selected().tab.id}/merge`,[{key:'sourceTabId',label:'Comanda de origem (será liberada)',type:'select',required:true,options:this.tabs().filter(t=>t.id!==this.selected().tab.id&&t.status==='Open').map(t=>({value:t.id,label:`#${t.number} · ${t.consumer}`}))}],{},'POST',async()=>{this.selected.set(await this.api.get('/tabs/'+this.selected().tab.id));this.tabs.set(await this.api.get('/tabs'));});}
 adjustStock(p:any){this.openForm('Movimentar estoque · '+p.name,`/products/${p.id}/stock`,[{key:'quantity',label:'Quantidade (negativa para perda/saída)',type:'number',step:'0.001',required:true},this.field('reason','Motivo da movimentação','textarea')],{quantity:0,reason:''});}
 receive(a:any){this.openForm('Receber fiado · '+a.customerName,`/receivables/${a.id}/receive`,[{...this.moneyField('amount','Valor recebido (R$)'),max:a.balance},{key:'method',label:'Forma de pagamento',type:'select',options:this.methods}],{amount:a.balance,method:'Cash'});}
 reverse(p:any){this.openForm('Estornar recebimento de fiado',`/payments/${p.id}/reverse`,[this.field('reason','Motivo do estorno','textarea')],{reason:''});}
 openCash(){this.openForm('Abrir caixa','/cash/open',[this.moneyField('initial','Saldo inicial em dinheiro (R$)')],{initial:0});}
 closeCash(){this.openForm('Conferir e fechar caixa','/cash/close',[this.moneyField('counted','Dinheiro contado (R$)')],{counted:0});}
 cashEntry(kind='Expense'){this.openForm('Registrar movimentação de caixa','/cash/movements',[{key:'kind',label:'Tipo',type:'select',options:[{value:'Expense',label:'Despesa'},{value:'Withdrawal',label:'Sangria'},{value:'Supply',label:'Suprimento'}]},this.moneyField('amount','Valor (R$)'),{key:'method',label:'Forma',type:'select',options:this.methods},this.field('description','Descrição','textarea')],{kind,amount:0,method:'Cash',description:''});}
 async showStatement(c:any){await this.run(async()=>{this.statement.set(await this.api.get(`/customers/${c.id}/statement`));});}
 shareStatement(){const s=this.statement();const text=`MEU BAR — Extrato de ${s.customer.name}\nSaldo devedor: ${this.money(s.balance)}\n`+s.accounts.map(a=>`Compra: ${this.money(a.original)} · Saldo: ${this.money(a.balance)}`).join('\n');window.open('https://wa.me/'+(s.customer.whatsApp||'').replace(/\D/g,'')+'?text='+encodeURIComponent(text),'_blank','noopener,noreferrer');}
 shareTab(){const s=this.selected();const text=`MEU BAR — Comanda #${s.tab.number}\n`+s.items.filter(i=>!i.cancelled).map(i=>`${i.quantity} × ${i.productName} — ${this.money(i.quantity*i.unitPrice)}`).join('\n')+`\nTotal: ${this.money(s.total)}`;window.open('https://wa.me/?text='+encodeURIComponent(text),'_blank','noopener,noreferrer');}
 print(){window.print();}
 money(v:number){return new Intl.NumberFormat('pt-BR',{style:'currency',currency:'BRL'}).format(v||0);}
 label(v:string){return ({Open:'Ocupada',Closing:'Aguardando fechamento',Free:'Livre',Closed:'Fechada',Cancelled:'Cancelada',Merged:'Unida',Receipt:'Recebimento',Reversal:'Estorno',Expense:'Despesa',Withdrawal:'Sangria',Supply:'Suprimento',Admin:'Administrador',Employee:'Funcionário'} as any)[v]||this.methods.find(m=>m.value===v)?.label||v;}
 bars(){let daily=this.data().daily||[];const groups=new Map<string,number>();for(const d of daily){let key=d.date;if(this.grouping==='month')key=key.slice(0,7);if(this.grouping==='week'){const date=new Date(key+'T12:00:00');date.setDate(date.getDate()-((date.getDay()+6)%7));key=date.toISOString().slice(0,10);}groups.set(key,(groups.get(key)||0)+d.total);}const max=Math.max(...groups.values(),1);return [...groups].map(([date,total])=>({date,total,width:total/max*100}));}
 overdue(a:any){return a.balance>0&&a.dueAt&&new Date(a.dueAt)<new Date();}
 productName(id:string){return this.products().find(p=>p.id===id)?.name||id;}
}
