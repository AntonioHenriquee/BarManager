using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
namespace MeuBar;
public class BarOperations(BarDb db) {
 public async Task<Tab> ActiveTab(Guid id) {
  var tab=await db.Tabs.SingleOrDefaultAsync(x=>x.Id==id)??throw new RuleException("Comanda não encontrada.",404);
  Rules.Check(tab.Status is "Open" or "Closing","Comanda já encerrada.",409); return tab;
 }
 public async Task<CashSession> OpenCash()=>await db.CashSessions.SingleOrDefaultAsync(x=>x.IsOpen)??throw new RuleException("Abra o caixa antes de receber pagamentos.",409);
 public async Task<object> OpenTab(OpenTabRequest r,Guid user) {
  if(r.TableId is not null) {
   Rules.Check(await db.Tables.AnyAsync(x=>x.Id==r.TableId&&x.Active),"Mesa não encontrada.",404);
   Rules.Check(!await db.Tabs.AnyAsync(x=>x.TableId==r.TableId&&(x.Status=="Open"||x.Status=="Closing")),"Mesa já ocupada.",409);
  }
  var tab=new Tab{TableId=r.TableId,Consumer=string.IsNullOrWhiteSpace(r.Consumer)?"Consumidor":Rules.Text(r.Consumer,"Consumidor"),Notes=Rules.Text(r.Notes,"Observações",1000,false),OpenedBy=user};
  db.Tabs.Add(tab); return tab;
 }
 public async Task<object> AddItem(Guid tabId,ItemRequest r,Guid user) {
  var tab=await ActiveTab(tabId); Rules.Check(tab.Status=="Open","Comanda aguardando fechamento.",409);
  var product=await db.Products.SingleOrDefaultAsync(x=>x.Id==r.ProductId&&x.Active)??throw new RuleException("Produto não encontrado.",404);
  Rules.Quantity(r.Quantity); Rules.Check(product.Stock>=r.Quantity,"Estoque insuficiente.",409);
  product.Stock-=r.Quantity;
  var item=new TabItem{TabId=tabId,ProductId=product.Id,ProductName=product.Name,Quantity=r.Quantity,UnitPrice=product.Price,UnitCost=product.Cost,AddedBy=user};
  db.Items.Add(item); db.StockMovements.Add(new StockMovement{ProductId=product.Id,TabItemId=item.Id,Quantity=-r.Quantity,Reason="Consumo em comanda",UserId=user});
  return new{item.Id,item.TabId,item.ProductName,item.Quantity,item.UnitPrice,item.AddedAt};
 }
 public async Task<object> CancelItem(Guid id,ReasonRequest r,Guid user) {
  var item=await db.Items.SingleOrDefaultAsync(x=>x.Id==id)??throw new RuleException("Item não encontrado.",404); await ActiveTab(item.TabId);
  Rules.Check(!item.Cancelled,"Item já cancelado.",409); item.Cancelled=true;item.CancelReason=Rules.Text(r.Reason,"Motivo",1000);
  var p=await db.Products.SingleAsync(x=>x.Id==item.ProductId);p.Stock+=item.Quantity;
  db.StockMovements.Add(new StockMovement{ProductId=p.Id,TabItemId=item.Id,Quantity=item.Quantity,Reason=item.CancelReason,UserId=user});return item;
 }
 public async Task<object> CloseTab(Guid id,CloseTabRequest r,ClaimsPrincipal user) {
  var tab=await ActiveTab(id); var uid=Rules.UserId(user);
  Rules.Check(r.Payments is not null&&r.Payments.Count<=20,"Pagamentos inválidos."); Rules.Money(r.CreditAmount);Rules.Money(r.Discount);
  if(r.Payments!.Count>0) Rules.Receive(user); if(r.Discount>0) Rules.Admin(user);
  var items=await db.Items.Where(x=>x.TabId==id&&!x.Cancelled).ToListAsync();Rules.Check(items.Count>0,"Comanda sem itens.");
  var gross=decimal.Round(items.Sum(x=>x.Quantity*x.UnitPrice),2,MidpointRounding.AwayFromZero);
  Rules.Check(r.Discount<=gross,"Desconto acima do total."); var total=gross-r.Discount;
  foreach(var pay in r.Payments){Rules.Money(pay.Amount,true);Rules.Method(pay.Method);}
  Rules.Check(r.Payments.Sum(x=>x.Amount)+r.CreditAmount==total,"Pagamentos e fiado devem corresponder exatamente ao total.");
  if(r.CreditAmount>0) {
   Rules.Check(r.CustomerId is not null,"Selecione o cliente para fiado.");
   var customer=await db.Customers.SingleOrDefaultAsync(x=>x.Id==r.CustomerId&&x.Active&&x.CreditAuthorized)??throw new RuleException("Cliente não autorizado para fiado.");
   var balance=await db.Receivables.Where(x=>x.CustomerId==customer.Id).SumAsync(x=>x.Balance);
   Rules.Check(balance+r.CreditAmount<=customer.CreditLimit,"Limite de crédito excedido.",409);
  }
  if(r.CustomerId is not null) Rules.Check(await db.Customers.AnyAsync(x=>x.Id==r.CustomerId&&x.Active),"Cliente inválido.");
  var sale=new Sale{TabId=id,CustomerId=r.CustomerId,UserId=uid,Total=total,Discount=r.Discount,Cost=decimal.Round(items.Sum(x=>x.Quantity*x.UnitCost),2,MidpointRounding.AwayFromZero)};
  db.Sales.Add(sale);
  if(r.CreditAmount>0) db.Receivables.Add(new Receivable{SaleId=sale.Id,CustomerId=r.CustomerId!.Value,Original=r.CreditAmount,Balance=r.CreditAmount,DueAt=r.DueAt?.ToUniversalTime()});
  if(r.Payments.Count>0) { var cash=await OpenCash(); foreach(var p in r.Payments) AddPayment(sale.Id,null,p.Amount,p.Method,uid,cash.Id); }
  tab.Status="Closed";tab.ClosedAt=DateTimeOffset.UtcNow;
  return new{sale.Id,sale.TabId,sale.Total,sale.Discount,sale.CreatedAt};
 }
 void AddPayment(Guid? saleId,Guid? receivableId,decimal amount,string method,Guid user,Guid cash) {
  var p=new Payment{SaleId=saleId,ReceivableId=receivableId,Amount=amount,Method=method,UserId=user,CashSessionId=cash}; db.Payments.Add(p);
  db.CashMovements.Add(new CashMovement{CashSessionId=cash,PaymentId=p.Id,UserId=user,Kind="Receipt",Method=method,Amount=amount,Description=receivableId is null?"Recebimento de venda":"Recebimento de fiado"});
 }
 public async Task<object> Receive(Guid id,ReceiveRequest r,Guid user) {
  Rules.Money(r.Amount,true);Rules.Method(r.Method);
  var account=await db.Receivables.SingleOrDefaultAsync(x=>x.Id==id)??throw new RuleException("Conta não encontrada.",404);
  Rules.Check(r.Amount<=account.Balance,"Recebimento acima do saldo devedor.",409);
  var cash=await OpenCash(); account.Balance-=r.Amount;AddPayment(account.SaleId,id,r.Amount,r.Method,user,cash.Id); return account;
 }
 public async Task<object> ReversePayment(Guid id,ReasonRequest r,Guid user) {
  var p=await db.Payments.SingleOrDefaultAsync(x=>x.Id==id)??throw new RuleException("Pagamento não encontrado.",404);
  Rules.Check(!p.Reversed,"Pagamento já estornado.",409);
  // Estorno de venda à vista requer fluxo específico de devolução; nunca reabre implicitamente uma venda.
  Rules.Check(p.ReceivableId is not null,"Estorno disponível somente para recebimentos de fiado. Devolução de venda exige fluxo próprio.",409);
  var cash=await OpenCash();var account=await db.Receivables.SingleAsync(x=>x.Id==p.ReceivableId);
  account.Balance+=p.Amount;p.Reversed=true;p.Reason=Rules.Text(r.Reason,"Motivo",1000);
  db.CashMovements.Add(new CashMovement{CashSessionId=cash.Id,UserId=user,PaymentId=p.Id,Kind="Reversal",Method=p.Method,Amount=-p.Amount,Description=p.Reason});return p;
 }
 public async Task<decimal> CashExpected(CashSession cash)=>cash.Initial+await db.CashMovements.Where(x=>x.CashSessionId==cash.Id&&x.Method=="Cash").SumAsync(x=>x.Amount);
}
