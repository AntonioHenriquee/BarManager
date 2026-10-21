using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
namespace MeuBar;
public static class Reports {
 static readonly TimeZoneInfo Zone=TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
 static DateTimeOffset Start(DateOnly date)=>new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue),Zone));
 public static async Task<IResult> Dashboard(BarDb db,BarOperations op) {
  var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,Zone).DateTime);var start=Start(today);var month=Start(new DateOnly(today.Year,today.Month,1));
  var sales=await db.Sales.Where(x=>x.CreatedAt>=month).ToListAsync();var received=await db.CashMovements.Where(x=>x.CreatedAt>=start&&(x.Kind=="Receipt"||x.Kind=="Reversal")).SumAsync(x=>x.Amount);
  var tabs=await db.Tabs.Where(x=>x.Status=="Open"||x.Status=="Closing").ToListAsync();var ids=tabs.Select(x=>x.Id).ToList();var items=await db.Items.Where(x=>ids.Contains(x.TabId)&&!x.Cancelled).ToListAsync();
  var cash=await db.CashSessions.SingleOrDefaultAsync(x=>x.IsOpen);var now=DateTimeOffset.UtcNow;
  return Results.Ok(new{soldToday=sales.Where(x=>x.CreatedAt>=start).Sum(x=>x.Total),receivedToday=received,soldMonth=sales.Sum(x=>x.Total),openTabs=tabs.Count,openTotal=decimal.Round(items.Sum(x=>x.Quantity*x.UnitPrice),2,MidpointRounding.AwayFromZero),creditPending=await db.Receivables.SumAsync(x=>x.Balance),creditOverdue=await db.Receivables.Where(x=>x.DueAt<now).SumAsync(x=>x.Balance),cashBalance=cash is null?0:await op.CashExpected(cash),grossProfit=sales.Sum(x=>x.Total-x.Cost),lowStock=await db.Products.Where(x=>x.Active&&x.Stock<=x.MinimumStock).Select(x=>new{x.Name,x.Stock,x.MinimumStock}).ToListAsync(),daily=sales.GroupBy(x=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.CreatedAt,Zone).DateTime)).OrderBy(x=>x.Key).Select(x=>new{date=x.Key,total=x.Sum(s=>s.Total)})});
 }
 public static async Task<object> Data(BarDb db,DateOnly? from,DateOnly? to) {
  var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,Zone).DateTime);var a=from??today.AddDays(-30);var b=to??today;Rules.Check(b>=a&&b.DayNumber-a.DayNumber<=366,"Selecione período de até 366 dias.");
  var start=Start(a);var end=Start(b.AddDays(1));
  var sales=await db.Sales.Where(x=>x.CreatedAt>=start&&x.CreatedAt<end).OrderByDescending(x=>x.CreatedAt).ToListAsync();
  var ids=sales.Select(x=>x.TabId).ToList();var items=await db.Items.Where(x=>ids.Contains(x.TabId)&&!x.Cancelled).ToListAsync();
  var movements=await db.CashMovements.Where(x=>x.CreatedAt>=start&&x.CreatedAt<end).ToListAsync();
  var users=await db.Users.ToDictionaryAsync(x=>x.Id,x=>x.Name);
  return new{from=a,to=b,totalSold=sales.Sum(x=>x.Total),grossProfit=sales.Sum(x=>x.Total-x.Cost),received=movements.Where(x=>x.Kind is "Receipt" or "Reversal").Sum(x=>x.Amount),expenses=-movements.Where(x=>x.Kind=="Expense").Sum(x=>x.Amount),sales,topProducts=items.GroupBy(x=>x.ProductId).Select(x=>new{name=x.First().ProductName,quantity=x.Sum(i=>i.Quantity),total=decimal.Round(x.Sum(i=>i.Quantity*i.UnitPrice),2,MidpointRounding.AwayFromZero)}).OrderByDescending(x=>x.quantity),byUser=sales.GroupBy(x=>x.UserId).Select(x=>new{name=users.GetValueOrDefault(x.Key,"Usuário"),total=x.Sum(s=>s.Total)}),byMethod=movements.Where(x=>x.Kind is "Receipt" or "Reversal").GroupBy(x=>x.Method).Select(x=>new{method=x.Key,total=x.Sum(m=>m.Amount)}),daily=sales.GroupBy(x=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.CreatedAt,Zone).DateTime)).OrderBy(x=>x.Key).Select(x=>new{date=x.Key,total=x.Sum(s=>s.Total)}),movements};
 }
 public static async Task<IResult> Report(BarDb db,DateOnly? from,DateOnly? to)=>Results.Ok(await Data(db,from,to));
 public static async Task<IResult> Csv(BarDb db,DateOnly? from,DateOnly? to) {
  var data=System.Text.Json.JsonSerializer.SerializeToElement(await Data(db,from,to),Writer.Json);var sb=new StringBuilder("Identificador;Data;Comanda;Total;Desconto;Custo;Lucro bruto estimado\r\n");
  foreach(var s in data.GetProperty("sales").EnumerateArray())sb.AppendLine(string.Join(';',s.GetProperty("id").GetString(),DateTimeOffset.Parse(s.GetProperty("createdAt").GetString()!,CultureInfo.InvariantCulture).ToString("dd/MM/yyyy HH:mm",CultureInfo.GetCultureInfo("pt-BR")),s.GetProperty("tabId").GetString(),s.GetProperty("total").GetDecimal().ToString("F2",CultureInfo.GetCultureInfo("pt-BR")),s.GetProperty("discount").GetDecimal().ToString("F2",CultureInfo.GetCultureInfo("pt-BR")),s.GetProperty("cost").GetDecimal().ToString("F2",CultureInfo.GetCultureInfo("pt-BR")),(s.GetProperty("total").GetDecimal()-s.GetProperty("cost").GetDecimal()).ToString("F2",CultureInfo.GetCultureInfo("pt-BR"))));
  return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),"text/csv; charset=utf-8","vendas-meu-bar.csv");
 }
}
