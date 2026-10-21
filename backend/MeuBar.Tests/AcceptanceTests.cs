using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeuBar;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MeuBar.Tests;
public class SystemFixture : IAsyncLifetime {
 public WebApplicationFactory<Program> Factory=null!;public HttpClient Client=null!;public string Connection="";public string ReplicaConnection="";public string RecoveryConnection="";
 public string Password="T!"+Guid.NewGuid().ToString("N");string baseConnection="";readonly List<string> databases=[];
 public async Task InitializeAsync(){
  baseConnection=Environment.GetEnvironmentVariable("MEUBAR_TEST_DATABASE")??throw new InvalidOperationException("Configure MEUBAR_TEST_DATABASE com PostgreSQL de testes (permissão CREATE DATABASE).");
  Connection=await NewDatabase();ReplicaConnection=await NewDatabase();RecoveryConnection=await NewDatabase();
  Factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>{b.UseEnvironment("Development");b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:Database",Connection},{"Database:Migrate","true"},{"Bootstrap:AdminPassword",Password},{"Security:SecureCookies","false"},{"Node:DeviceId","test-authority"},{"Node:Mode","Authority"},{"Sync:RemoteUrl",""}}));});
  Client=Factory.CreateClient();var login=await Send(Client,"/auth/login",new{login="admin",password=Password});Assert.Equal(HttpStatusCode.OK,login.StatusCode);
  await Post("/cash/open",new{operationId=Guid.NewGuid(),initial=0});
 }
 public async Task<string> NewDatabase(){var name="meubar_test_"+Guid.NewGuid().ToString("N");await using var connection=new NpgsqlConnection(baseConnection);await connection.OpenAsync();await using var command=new NpgsqlCommand($"CREATE DATABASE {name}",connection);await command.ExecuteNonQueryAsync();databases.Add(name);return new NpgsqlConnectionStringBuilder(baseConnection){Database=name}.ConnectionString;}
 public async Task DisposeAsync(){Client.Dispose();await Factory.DisposeAsync();NpgsqlConnection.ClearAllPools();await using var c=new NpgsqlConnection(baseConnection);await c.OpenAsync();foreach(var name in databases){await using var cmd=new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)",c);await cmd.ExecuteNonQueryAsync();}}
 public BarDb Db(string? connection=null)=>new(new DbContextOptionsBuilder<BarDb>().UseNpgsql(connection??Connection).Options);
 public static Task<HttpResponseMessage> Send(HttpClient c,string path,object body,HttpMethod? method=null){var r=new HttpRequestMessage(method??HttpMethod.Post,"/api"+path){Content=JsonContent.Create(body)};r.Headers.Add("X-MeuBar","1");return c.SendAsync(r);}
 public async Task<JsonNode> Post(string path,object body){var r=await Send(Client,path,body);Assert.True(r.IsSuccessStatusCode,$"{path}: {r.StatusCode} {await r.Content.ReadAsStringAsync()}");return JsonNode.Parse(await r.Content.ReadAsStringAsync())!;}
 public async Task<JsonNode> Get(string path){var r=await Client.GetAsync("/api"+path);r.EnsureSuccessStatusCode();return JsonNode.Parse(await r.Content.ReadAsStringAsync())!;}
 public async Task<Guid> Product(decimal price=10,decimal stock=50){var p=await Post("/products",new{operationId=Guid.NewGuid(),code=Guid.NewGuid().ToString(),name="Cerveja teste",category="Bebidas",unit="un",barcode="",supplier="",cost=4,price,minimumStock=5,active=true});var id=p["id"]!.GetValue<Guid>();await Post($"/products/{id}/stock",new{operationId=Guid.NewGuid(),quantity=stock,reason="Entrada inicial"});return id;}
 public async Task<Guid> Table(){var t=await Post("/tables",new{operationId=Guid.NewGuid(),name="Mesa "+Guid.NewGuid(),description=""});return t["id"]!.GetValue<Guid>();}
 public async Task<Guid> Tab(Guid? table=null,string consumer="Consumidor"){var t=await Post("/tabs",new{operationId=Guid.NewGuid(),tableId=table,consumer,notes=""});return t["id"]!.GetValue<Guid>();}
 public Task<JsonNode> Item(Guid tab,Guid product,decimal quantity)=>Post($"/tabs/{tab}/items",new{operationId=Guid.NewGuid(),productId=product,quantity});
 public Task<JsonNode> Close(Guid tab,decimal cash,decimal credit=0,Guid? customer=null,Guid? operation=null)=>Post($"/tabs/{tab}/close",new{operationId=operation??Guid.NewGuid(),payments=cash>0?new[]{new{method="Cash",amount=cash}}:[],creditAmount=credit,discount=0,customerId=customer,dueAt=(DateTimeOffset?)null});
 public async Task<Guid> Customer(decimal limit=200){var c=await Post("/customers",new{operationId=Guid.NewGuid(),name="João teste",nickname="",phone="",whatsApp="",address="",notes="",creditLimit=limit,creditAuthorized=true,active=true});return c["id"]!.GetValue<Guid>();}
}
public class AcceptanceTests(SystemFixture f) : IClassFixture<SystemFixture> {
 [Fact] public async Task TableCanBeReusedAndSaleRemainsInReports(){var table=await f.Table();var p=await f.Product();var first=await f.Tab(table,"Cliente A");await f.Item(first,p,5);var sale=await f.Close(first,50);var tables=await f.Get("/tables");Assert.Equal("Free",tables.AsArray().Single(x=>x!["id"]!.GetValue<Guid>()==table)!["status"]!.GetValue<string>());var second=await f.Tab(table,"Cliente B");Assert.NotEqual(first,second);var detail=await f.Get($"/tabs/{second}");Assert.Empty(detail["items"]!.AsArray());var report=await f.Get("/reports");Assert.Contains(report["sales"]!.AsArray(),s=>s!["id"]!.GetValue<Guid>()==sale["id"]!.GetValue<Guid>());}
 [Fact] public async Task PartialCreditPaymentPreservesHistory(){var customer=await f.Customer();var p=await f.Product(20);var tab=await f.Tab();await f.Item(tab,p,5);await f.Close(tab,0,100,customer);var statement=await f.Get($"/customers/{customer}/statement");var account=statement["accounts"]![0]!["id"]!.GetValue<Guid>();await f.Post($"/receivables/{account}/receive",new{operationId=Guid.NewGuid(),amount=40,method="Pix"});statement=await f.Get($"/customers/{customer}/statement");Assert.Equal(60,statement["balance"]!.GetValue<decimal>());Assert.Single(statement["sales"]!.AsArray());Assert.Single(statement["payments"]!.AsArray());Assert.Equal(100,statement["accounts"]![0]!["original"]!.GetValue<decimal>());}
 [Fact] public async Task StockIsDeductedOnceAtConsumption(){var p=await f.Product(10,50);var tab=await f.Tab();await f.Item(tab,p,5);await f.Close(tab,50);await using var db=f.Db();Assert.Equal(45,(await db.Products.FindAsync(p))!.Stock);Assert.Single(await db.StockMovements.Where(x=>x.ProductId==p&&x.Quantity<0).ToListAsync());}
 [Fact] public async Task EmployeeCannotChangePriceDeleteProductsOrReadReports(){var login="employee"+Guid.NewGuid().ToString("N");var password="X!"+Guid.NewGuid().ToString("N");await f.Post("/users",new{operationId=Guid.NewGuid(),name="Funcionário",login,password,role="Employee",canReceive=false,canCloseCash=false,active=true});using var employee=f.Factory.CreateClient();Assert.Equal(HttpStatusCode.OK,(await SystemFixture.Send(employee,"/auth/login",new{login,password})).StatusCode);var p=await f.Product();Assert.Equal(HttpStatusCode.Forbidden,(await SystemFixture.Send(employee,$"/products/{p}",new{operationId=Guid.NewGuid(),code="x",name="Produto",category="",unit="un",barcode="",supplier="",cost=1,price=1,minimumStock=0,active=true,version=2},HttpMethod.Put)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await employee.DeleteAsync($"/api/products/{p}")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await employee.GetAsync("/api/reports")).StatusCode);var products=JsonNode.Parse(await employee.GetStringAsync("/api/products"))!;Assert.All(products.AsArray(),x=>Assert.Null(x!["cost"]));}
 [Fact] public async Task OfflineQueueReplaysIdempotentlyAndRestoresIntoNewDatabase(){var p=await f.Product();var tab=await f.Tab();await f.Item(tab,p,3);await f.Close(tab,30);await using var local=f.Db();var queue=await local.Outbox.OrderBy(x=>x.Sequence).AsNoTracking().ToListAsync();Assert.NotEmpty(queue);Assert.Contains(queue,x=>x.SentAt is null);
  await using var replica=f.Db(f.ReplicaConnection);await replica.Database.MigrateAsync();var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Node:Mode","Replica"},{"Sync:AuthorityDeviceId","test-authority"}}).Build();
  foreach(var op in queue){var env=new Envelope(op.Sequence,op.Id,op.DeviceId,op.Payload);await new Replicator(replica,config).Apply(env);await new Replicator(replica,config).Apply(env);replica.ChangeTracker.Clear();}
  Assert.Equal(await local.Sales.CountAsync(),await replica.Sales.CountAsync());Assert.Equal(await local.Payments.CountAsync(),await replica.Payments.CountAsync());Assert.Equal(await local.Operations.CountAsync(),await replica.Operations.CountAsync());
  await using var recovered=f.Db(f.RecoveryConnection);await recovered.Database.MigrateAsync();foreach(var op in queue){await new Replicator(recovered,config).Apply(new Envelope(op.Sequence,op.Id,op.DeviceId,op.Payload));recovered.ChangeTracker.Clear();}
  Assert.Equal(await local.Products.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Stock,x.Price}).ToListAsync(),await recovered.Products.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Stock,x.Price}).ToListAsync());Assert.Equal(await local.Customers.CountAsync(),await recovered.Customers.CountAsync());Assert.Equal(await local.Tabs.CountAsync(),await recovered.Tabs.CountAsync());Assert.Equal(await local.Payments.SumAsync(x=>x.Amount),await recovered.Payments.SumAsync(x=>x.Amount));
  var bad=queue[0];await Assert.ThrowsAsync<RuleException>(()=>new Replicator(replica,config).Apply(new Envelope(bad.Sequence,Guid.NewGuid(),bad.DeviceId,bad.Payload)));
 }
 [Fact] public async Task ConcurrentOpenAcceptsOnlyOneTab(){var table=await f.Table();var tasks=Enumerable.Range(0,2).Select(_=>SystemFixture.Send(f.Client,"/tabs",new{operationId=Guid.NewGuid(),tableId=table,consumer="Consumidor",notes=""}));var responses=await Task.WhenAll(tasks);Assert.Single(responses,x=>x.IsSuccessStatusCode);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);await using var db=f.Db();Assert.Single(await db.Tabs.Where(x=>x.TableId==table&&x.Status=="Open").ToListAsync());}
 [Fact] public async Task DuplicatePaymentAndClosureDoNotDuplicateFinancialMovements(){var customer=await f.Customer();var p=await f.Product(20);var tab=await f.Tab();await f.Item(tab,p,5);var closeId=Guid.NewGuid();await f.Close(tab,0,100,customer,closeId);await f.Close(tab,0,100,customer,closeId);var statement=await f.Get($"/customers/{customer}/statement");var id=statement["accounts"]![0]!["id"]!.GetValue<Guid>();var body=new{operationId=Guid.NewGuid(),amount=40,method="Cash"};await f.Post($"/receivables/{id}/receive",body);await f.Post($"/receivables/{id}/receive",body);await using var db=f.Db();Assert.Single(await db.Payments.Where(x=>x.ReceivableId==id).ToListAsync());var pay=await db.Payments.SingleAsync(x=>x.ReceivableId==id);Assert.Single(await db.CashMovements.Where(x=>x.PaymentId==pay.Id).ToListAsync());Assert.Equal(60,(await db.Receivables.FindAsync(id))!.Balance);Assert.Equal(HttpStatusCode.Conflict,(await SystemFixture.Send(f.Client,$"/receivables/{id}/receive",new{body.operationId,amount=30,method="Cash"})).StatusCode);}
 [Fact] public async Task FailedClosureRollsBackAllFinancialWritesAndLeavesNoSequenceGap(){var p=await f.Product();var tab=await f.Tab();await f.Item(tab,p,1);var response=await SystemFixture.Send(f.Client,$"/tabs/{tab}/close",new{operationId=Guid.NewGuid(),payments=new[]{new{method="Cash",amount=9}},creditAmount=0,discount=0,customerId=(Guid?)null,dueAt=(DateTimeOffset?)null});Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);await using var db=f.Db();Assert.False(await db.Sales.AnyAsync(x=>x.TabId==tab));Assert.Equal("Open",(await db.Tabs.FindAsync(tab))!.Status);await f.Close(tab,10);var sequence=await db.Outbox.OrderBy(x=>x.Sequence).Select(x=>x.Sequence).ToListAsync();Assert.Equal(Enumerable.Range(1,sequence.Count).Select(x=>(long)x),sequence);}
 [Fact] public async Task CancellationRestoresStockAndRemainsAudited(){var p=await f.Product();var tab=await f.Tab();var item=await f.Item(tab,p,5);await f.Post($"/items/{item["id"]!.GetValue<Guid>()}/cancel",new{operationId=Guid.NewGuid(),reason="Pedido incorreto"});await using var db=f.Db();Assert.Equal(50,(await db.Products.FindAsync(p))!.Stock);Assert.True((await db.Items.FindAsync(item["id"]!.GetValue<Guid>()))!.Cancelled);Assert.Contains(await db.Audits.ToListAsync(),a=>a.Action=="item.cancel");}
 [Fact] public async Task UnauthenticatedAndCsrfRequestsAreRejected(){using var client=f.Factory.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/products")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.PostAsJsonAsync("/api/tables",new{operationId=Guid.NewGuid(),name="invasão",description=""})).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await SystemFixture.Send(client,"/sync/ingest",new Envelope(1,Guid.NewGuid(),"rogue","[]"))).StatusCode);}
 [Fact] public async Task CreditLimitAndOverpaymentAreRejected(){var customer=await f.Customer(50);var p=await f.Product(20);var tab=await f.Tab();await f.Item(tab,p,5);var response=await SystemFixture.Send(f.Client,$"/tabs/{tab}/close",new{operationId=Guid.NewGuid(),payments=Array.Empty<object>(),creditAmount=100,discount=0,customerId=customer,dueAt=(DateTimeOffset?)null});Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);await using var db=f.Db();Assert.False(await db.Receivables.AnyAsync(x=>x.CustomerId==customer));}

 [Fact] public async Task MixedPaymentSeparatesCreditAndReversalRestoresDebt(){
  var customer=await f.Customer();var p=await f.Product();var tab=await f.Tab();await f.Item(tab,p,7);
  var sale=await f.Post($"/tabs/{tab}/close",new{operationId=Guid.NewGuid(),payments=new[]{new{method="Cash",amount=20},new{method="Pix",amount=20}},creditAmount=30,discount=0,customerId=customer,dueAt=DateTimeOffset.UtcNow.AddDays(-1)});
  var saleId=sale["id"]!.GetValue<Guid>();await using var db=f.Db();var debt=await db.Receivables.SingleAsync(x=>x.SaleId==saleId);Assert.Equal(30,debt.Balance);Assert.Equal(40,await db.Payments.Where(x=>x.SaleId==saleId).SumAsync(x=>x.Amount));
  var opId=Guid.NewGuid();await f.Post($"/receivables/{debt.Id}/receive",new{operationId=opId,amount=10,method="Debit"});var payment=await db.Payments.SingleAsync(x=>x.ReceivableId==debt.Id);
  await f.Post($"/payments/{payment.Id}/reverse",new{operationId=Guid.NewGuid(),reason="Conferência do recebimento"});db.ChangeTracker.Clear();Assert.Equal(30,(await db.Receivables.FindAsync(debt.Id))!.Balance);Assert.True((await db.Payments.FindAsync(payment.Id))!.Reversed);Assert.Equal(0,await db.CashMovements.Where(x=>x.PaymentId==payment.Id).SumAsync(x=>x.Amount));
 }
 [Fact] public async Task HistoricalItemPriceAndCostSurviveProductChange(){
  var p=await f.Product();var tab=await f.Tab();await f.Item(tab,p,5);var products=await f.Get("/products");var current=products.AsArray().Single(x=>x!["id"]!.GetValue<Guid>()==p)!;
  var response=await SystemFixture.Send(f.Client,$"/products/{p}",new{operationId=Guid.NewGuid(),code=current["code"]!.GetValue<string>(),name="Produto atualizado",category="",unit="un",barcode="",supplier="",cost=99,price=150,minimumStock=5,active=true,version=current["version"]!.GetValue<long>()},HttpMethod.Put);Assert.True(response.IsSuccessStatusCode);
  var sale=await f.Close(tab,50);await using var db=f.Db();var saved=await db.Sales.FindAsync(sale["id"]!.GetValue<Guid>());Assert.Equal(50,saved!.Total);Assert.Equal(20,saved.Cost);
 }
 [Fact] public async Task CashClosingCanBeDelegatedWithoutGrantingReceiptsOrAdmin(){
  var login="cashier"+Guid.NewGuid().ToString("N");var password="X!"+Guid.NewGuid().ToString("N");
  await f.Post("/users",new{operationId=Guid.NewGuid(),name="Conferente",login,password,role="Employee",canReceive=false,canCloseCash=true,active=true});
  using var employee=f.Factory.CreateClient();Assert.Equal(HttpStatusCode.OK,(await SystemFixture.Send(employee,"/auth/login",new{login,password})).StatusCode);
  Assert.Equal(HttpStatusCode.OK,(await employee.GetAsync("/api/cash")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await employee.GetAsync("/api/payments")).StatusCode);
  var state=await f.Get("/cash");var counted=state["expected"]!.GetValue<decimal>()+3;
  var result=await SystemFixture.Send(employee,"/cash/close",new{operationId=Guid.NewGuid(),counted});Assert.Equal(HttpStatusCode.OK,result.StatusCode);
  var closed=JsonNode.Parse(await result.Content.ReadAsStringAsync())!;Assert.Equal(3,closed["counted"]!.GetValue<decimal>()-closed["expected"]!.GetValue<decimal>());
  await f.Post("/cash/open",new{operationId=Guid.NewGuid(),initial=0});
 }
 [Fact] public async Task TransportFailureKeepsDurableQueueAndRetryAcknowledgesExactlyOnce(){
  var sourceConnection=await f.NewDatabase();var targetConnection=await f.NewDatabase();
  await using(var db=f.Db(sourceConnection)){
   await db.Database.MigrateAsync();var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Node:DeviceId","transport-authority"}}).Build();
   var user=new User{Name="Teste",Login="transport",PasswordHash="test-only",Role="Admin"};
   await new Writer(db,config).Run(Guid.NewGuid(),user.Id,"bootstrap",new{user.Login},()=>{db.Users.Add(user);return Task.FromResult<object>(new{user.Id});});
  }
  await using(var db=f.Db(targetConnection))await db.Database.MigrateAsync();
  var replicaConfig=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Node:Mode","Replica"},{"Sync:AuthorityDeviceId","transport-authority"}}).Build();
  var transport=new FaultTransport(targetConnection,replicaConfig);
  var services=new ServiceCollection();services.AddDbContext<BarDb>(o=>o.UseNpgsql(sourceConnection));using var provider=services.BuildServiceProvider();
  var workerConfig=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Sync:RemoteUrl","https://replica.test"},{"Sync:Key",new string('k',32)},{"Sync:IntervalSeconds","1"}}).Build();
  using var worker=new SyncWorker(provider.GetRequiredService<IServiceScopeFactory>(),transport,workerConfig,NullLogger<SyncWorker>.Instance);
  await worker.StartAsync(CancellationToken.None);
  try {
   await Wait(async()=>{await using var db=f.Db(sourceConnection);return (await db.SyncHealth.FindAsync(1))?.Error.Length>0;});
   await using(var db=f.Db(sourceConnection))Assert.Equal(1,await db.Outbox.CountAsync(x=>x.SentAt==null));
   transport.Online=true;
   await Wait(async()=>{await using var db=f.Db(sourceConnection);return await db.Outbox.AllAsync(x=>x.SentAt!=null);});
   await using(var replica=f.Db(targetConnection)){Assert.Equal(1,await replica.Operations.CountAsync());Assert.Equal(1,await replica.Outbox.CountAsync());Assert.Single(await replica.Users.ToListAsync());}
   Assert.True(transport.Attempts>=3); // offline, confirmação perdida após commit, retry idempotente
  } finally {await worker.StopAsync(CancellationToken.None);}
 }
 static async Task Wait(Func<Task<bool>> check){for(var i=0;i<100;i++){if(await check())return;await Task.Delay(100);}Assert.Fail("Tempo excedido aguardando sincronização.");}

}

public class FaultTransport(string connection,IConfiguration config) : HttpMessageHandler,IHttpClientFactory {
 public volatile bool Online;public int Attempts;bool lostAcknowledgement;
 public HttpClient CreateClient(string name)=>new(this,false);
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
  Attempts++;if(!Online)throw new HttpRequestException("Internet indisponível no teste");
  if(request.Method==HttpMethod.Post){
   var envelope=(await request.Content!.ReadFromJsonAsync<Envelope>(ct))!;
   await using var db=new BarDb(new DbContextOptionsBuilder<BarDb>().UseNpgsql(connection).Options);
   await new Replicator(db,config).Apply(envelope);
   if(!lostAcknowledgement){lostAcknowledgement=true;throw new HttpRequestException("Resposta perdida após commit remoto");}
  }
  return new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{status="ok"})};
 }
}
