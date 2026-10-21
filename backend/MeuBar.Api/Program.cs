using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using MeuBar;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder=WebApplication.CreateBuilder(args);
var mode=builder.Configuration["Node:Mode"]??"Authority";
Rules.Check(mode is "Authority" or "Replica","Node__Mode deve ser Authority ou Replica.");
if(mode=="Replica") Rules.Check((builder.Configuration["Sync:Key"]??"").Length>=32,"Réplica exige Sync__Key com pelo menos 32 caracteres.");
var remote=builder.Configuration["Sync:RemoteUrl"];
if(!string.IsNullOrEmpty(remote)) {
 Rules.Check(Uri.TryCreate(remote,UriKind.Absolute,out var uri)&&uri.Scheme=="https","Sync__RemoteUrl exige uma URL HTTPS absoluta.");
 Rules.Check((builder.Configuration["Sync:Key"]??"").Length>=32,"Sincronização exige Sync__Key com pelo menos 32 caracteres.");
}
if(!builder.Environment.IsDevelopment()) Rules.Check(builder.Configuration.GetValue("Security:SecureCookies",true)||string.IsNullOrEmpty(builder.Configuration["PublicDomain"]),"Acesso remoto exige cookies seguros.");
builder.Services.AddDbContext<BarDb>(o=>o.UseNpgsql(builder.Configuration.GetConnectionString("Database")??throw new InvalidOperationException("Configure ConnectionStrings__Database.")));
builder.Services.AddScoped<Writer>();builder.Services.AddScoped<BarOperations>();builder.Services.AddScoped<Replicator>();
builder.Services.AddSingleton<IPasswordHasher<User>,PasswordHasher<User>>();
builder.Services.AddAuthentication("Session").AddScheme<AuthenticationSchemeOptions,BarAuth>("Session",null);
builder.Services.AddAuthorization(o=>{o.AddPolicy("Admin",p=>p.RequireRole("Admin"));o.AddPolicy("CashAccess",p=>p.RequireAssertion(c=>c.User.IsInRole("Admin")||c.User.HasClaim("receive","true")||c.User.HasClaim("closeCash","true")));o.AddPolicy("Receive",p=>p.RequireAssertion(c=>c.User.IsInRole("Admin")||c.User.HasClaim("receive","true")));});
builder.Services.AddRateLimiter(o=>{o.RejectionStatusCode=429;o.AddPolicy("login",ctx=>RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=10,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));});
builder.Services.AddHttpClient();builder.Services.AddHostedService<SyncWorker>();builder.Services.AddOpenApi();
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=2_000_000);
var app=builder.Build();
app.Use(async(ctx,next)=>{
 ctx.Response.Headers["X-Content-Type-Options"]="nosniff";ctx.Response.Headers["Referrer-Policy"]="same-origin";
 ctx.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
 ctx.Response.Headers.CacheControl="no-store";
 try {
  if(ctx.Request.Path.StartsWithSegments("/api")&&ctx.Request.Method is not ("GET" or "HEAD" or "OPTIONS")&&!ctx.Request.Path.StartsWithSegments("/api/sync/ingest")&&!ctx.Request.Path.StartsWithSegments("/api/backup/status")) Rules.Check(ctx.Request.Headers["X-MeuBar"]=="1","Cabeçalho de segurança ausente.",403);
  await next();
 } catch(RuleException ex) {ctx.Response.StatusCode=ex.Status;await ctx.Response.WriteAsJsonAsync(new{error=ex.Message});}
 catch(BadHttpRequestException ex){ctx.Response.StatusCode=ex.StatusCode;await ctx.Response.WriteAsJsonAsync(new{error="Requisição inválida. Verifique os campos, o formato e o tamanho dos dados."});}
 catch(DbUpdateConcurrencyException){ctx.Response.StatusCode=409;await ctx.Response.WriteAsJsonAsync(new{error="Registro modificado por outra operação. Atualize e tente novamente."});}
 catch(DbUpdateException ex) when(ex.InnerException is PostgresException p&&p.SqlState is "23505" or "23503" or "23514") {ctx.Response.StatusCode=409;await ctx.Response.WriteAsJsonAsync(new{error="Operação conflita com um registro existente ou viola a integridade dos dados."});}
 catch(Exception ex) {app.Logger.LogError("Falha na requisição {Path}: {Type}",ctx.Request.Path,ex.GetType().Name);ctx.Response.StatusCode=500;await ctx.Response.WriteAsJsonAsync(new{error="Falha interna. A operação não foi confirmada; consulte o histórico antes de tentar novamente."});}
});
app.UseRateLimiter();app.UseAuthentication();app.UseAuthorization();
app.MapGet("/health",async(BarDb db)=>await db.Database.CanConnectAsync()?Results.Ok(new{status="ok"}):Results.StatusCode(503));
app.MapGet("/api/auth/me",(ClaimsPrincipal user)=>Results.Ok(new{id=Rules.UserId(user),name=user.Identity!.Name,role=user.FindFirstValue(ClaimTypes.Role),canReceive=user.IsInRole("Admin")||user.HasClaim("receive","true"),canCloseCash=user.IsInRole("Admin")||user.HasClaim("closeCash","true")})).RequireAuthorization();
app.MapPost("/api/auth/login",async(LoginRequest r,BarDb db,IPasswordHasher<User> hasher,HttpContext ctx,IConfiguration config)=>{
 Rules.Text(r.Login,"Login",80);Rules.Text(r.Password,"Senha",128);
 var u=await db.Users.SingleOrDefaultAsync(x=>x.Login==r.Login.Trim().ToLowerInvariant()&&x.Active);
 // Hash substituto também para usuário inexistente, reduzindo distinção por tempo.
 var dummy=new User();var fallback=hasher.HashPassword(dummy,"not-a-real-account-password");
 var valid=hasher.VerifyHashedPassword(u??dummy,u?.PasswordHash??fallback,r.Password)!=PasswordVerificationResult.Failed;
 Rules.Check(u is not null&&valid,"Usuário ou senha inválidos.",401);
 var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
 db.Sessions.Add(new Session{UserId=u!.Id,TokenHash=Rules.Hash(token),ExpiresAt=DateTimeOffset.UtcNow.AddHours(8)});
 await db.SaveChangesAsync();
 ctx.Response.Cookies.Append("meubar_session",token,new CookieOptions{HttpOnly=true,Secure=config.GetValue("Security:SecureCookies",true),SameSite=SameSiteMode.Strict,MaxAge=TimeSpan.FromHours(8),Path="/"});return Results.Ok(new{u.Name,u.Role});
}).RequireRateLimiting("login");
app.MapPost("/api/auth/logout",async(HttpContext ctx,BarDb db)=>{var token=ctx.Request.Cookies["meubar_session"];if(token is not null){var hash=Rules.Hash(token);await db.Sessions.Where(x=>x.TokenHash==hash).ExecuteDeleteAsync();}ctx.Response.Cookies.Delete("meubar_session");return Results.NoContent();}).RequireAuthorization();
var api=app.MapGroup("/api").RequireAuthorization();
IResult Json(string value)=>Results.Content(value,"application/json",Encoding.UTF8);
api.MapGet("/products",async(BarDb db,ClaimsPrincipal user)=>{
 var rows=await db.Products.OrderBy(x=>x.Name).ToListAsync();return Results.Ok(rows.Select(x=>new{x.Id,x.Version,x.Code,x.Name,x.Category,x.Unit,x.Barcode,x.Supplier,x.Price,x.Stock,x.MinimumStock,x.Active,cost=user.IsInRole("Admin")?(decimal?)x.Cost:null}));
});
api.MapPost("/products",async(ProductRequest r,Writer w,BarDb db,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"product.create",r,()=>{
 var p=new Product();UpdateProduct(p,r);db.Products.Add(p);return Task.FromResult<object>(p);
}))).RequireAuthorization("Admin");
api.MapPut("/products/{id:guid}",async(Guid id,ProductRequest r,Writer w,BarDb db,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"product.update",new{id,r},async()=>{
 var p=await db.Products.FindAsync(id)??throw new RuleException("Produto não encontrado.",404);Rules.Check(p.Version==r.Version,"Produto alterado. Atualize a página.",409);UpdateProduct(p,r);return p;
}))).RequireAuthorization("Admin");
// Exclusão física não é permitida; desative para preservar histórico.
api.MapDelete("/products/{id:guid}",()=>Results.Conflict(new{error="Desative o produto; registros históricos são preservados."})).RequireAuthorization("Admin");
api.MapPost("/products/{id:guid}/stock",async(Guid id,StockRequest r,Writer w,BarDb db,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"stock.adjust",new{id,r},async()=>{
 Rules.Quantity(r.Quantity,false);Rules.Check(r.Quantity!=0,"Informe uma quantidade diferente de zero.");var p=await db.Products.FindAsync(id)??throw new RuleException("Produto não encontrado.",404);Rules.Check(p.Stock+r.Quantity>=0,"Saldo de estoque não pode ficar negativo.");p.Stock+=r.Quantity;
 db.StockMovements.Add(new StockMovement{ProductId=id,Quantity=r.Quantity,Reason=Rules.Text(r.Reason,"Motivo",1000),UserId=Rules.UserId(u)});return p;
}))).RequireAuthorization("Admin");
api.MapGet("/stock",async(BarDb db)=>await db.StockMovements.OrderByDescending(x=>x.CreatedAt).Take(500).ToListAsync()).RequireAuthorization("Admin");
api.MapGet("/customers",async(BarDb db,ClaimsPrincipal u)=>u.IsInRole("Admin")?Results.Ok(await db.Customers.OrderBy(x=>x.Name).ToListAsync()):Results.Ok(await db.Customers.Where(x=>x.Active&&x.CreditAuthorized).Select(x=>new{x.Id,x.Name}).ToListAsync()));
api.MapPost("/customers",async(CustomerRequest r,Writer w,BarDb db,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"customer.create",r,()=>{var c=new Customer();UpdateCustomer(c,r);db.Customers.Add(c);return Task.FromResult<object>(c);}))).RequireAuthorization("Admin");
api.MapPut("/customers/{id:guid}",async(Guid id,CustomerRequest r,Writer w,BarDb db,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"customer.update",new{id,r},async()=>{var c=await db.Customers.FindAsync(id)??throw new RuleException("Cliente não encontrado.",404);Rules.Check(c.Version==r.Version,"Cliente alterado. Atualize.",409);UpdateCustomer(c,r);return c;}))).RequireAuthorization("Admin");
api.MapGet("/customers/{id:guid}/statement",async(Guid id,BarDb db)=>{
 var c=await db.Customers.FindAsync(id);if(c is null)return Results.NotFound();
 var sales=await db.Sales.Where(x=>x.CustomerId==id).OrderByDescending(x=>x.CreatedAt).ToListAsync();var accounts=await db.Receivables.Where(x=>x.CustomerId==id).ToListAsync();var ids=accounts.Select(x=>x.Id).ToList();
 return Results.Ok(new{customer=c,sales,accounts,payments=await db.Payments.Where(x=>x.ReceivableId!=null&&ids.Contains(x.ReceivableId.Value)).OrderByDescending(x=>x.CreatedAt).ToListAsync(),balance=accounts.Sum(x=>x.Balance)});
}).RequireAuthorization("Admin");
api.MapGet("/tables",async(BarDb db)=>{
 var tables=await db.Tables.Where(x=>x.Active).OrderBy(x=>x.Name).ToListAsync();var tabs=await db.Tabs.Where(x=>x.Status=="Open"||x.Status=="Closing").ToListAsync();var items=await db.Items.Where(x=>!x.Cancelled).Join(db.Tabs.Where(x=>x.Status=="Open"||x.Status=="Closing"),i=>i.TabId,t=>t.Id,(i,t)=>i).ToListAsync();
 return tables.Select(t=>{var tab=tabs.SingleOrDefault(x=>x.TableId==t.Id);return new{t.Id,t.Name,t.Description,status=tab?.Status??"Free",tab,total=decimal.Round(items.Where(i=>i.TabId==tab?.Id).Sum(i=>i.Quantity*i.UnitPrice),2,MidpointRounding.AwayFromZero)};});
});
api.MapPost("/tables",async(TableRequest r,Writer w,BarDb db,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"table.create",r,()=>{var t=new ServiceTable{Name=Rules.Text(r.Name,"Nome",80),Description=Rules.Text(r.Description,"Descrição",200,false)};db.Tables.Add(t);return Task.FromResult<object>(t);}))).RequireAuthorization("Admin");
api.MapGet("/tabs",async(BarDb db)=>await db.Tabs.Where(x=>x.Status=="Open"||x.Status=="Closing").OrderBy(x=>x.Number).ToListAsync());
api.MapGet("/tabs/{id:guid}",async(Guid id,BarDb db)=>{var tab=await db.Tabs.FindAsync(id);if(tab is null)return Results.NotFound();var items=await db.Items.Where(x=>x.TabId==id).OrderBy(x=>x.AddedAt).ToListAsync();return Results.Ok(new{tab,items=items.Select(x=>new{x.Id,x.ProductId,x.ProductName,x.Quantity,x.UnitPrice,x.Cancelled,x.CancelReason,x.AddedAt,x.AddedBy}),total=decimal.Round(items.Where(x=>!x.Cancelled).Sum(x=>x.Quantity*x.UnitPrice),2,MidpointRounding.AwayFromZero)});});
api.MapPost("/tabs",async(OpenTabRequest r,Writer w,BarOperations op,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"tab.open",r,()=>op.OpenTab(r,Rules.UserId(u)))));
api.MapPost("/tabs/{id:guid}/items",async(Guid id,ItemRequest r,Writer w,BarOperations op,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"tab.item",new{id,r},()=>op.AddItem(id,r,Rules.UserId(u)))));
api.MapPost("/items/{id:guid}/cancel",async(Guid id,ReasonRequest r,Writer w,BarOperations op,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"item.cancel",new{id,r},()=>op.CancelItem(id,r,Rules.UserId(u))))).RequireAuthorization("Admin");
api.MapPost("/tabs/{id:guid}/request-close",async(Guid id,OperationRequest r,Writer w,BarOperations op,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"tab.request-close",new{id,r},async()=>{var tab=await op.ActiveTab(id);tab.Status="Closing";return tab;})));
api.MapPost("/tabs/{id:guid}/close",async(Guid id,CloseTabRequest r,Writer w,BarOperations op,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"tab.close",new{id,r},()=>op.CloseTab(id,r,u))));
api.MapPost("/tabs/{id:guid}/cancel",async(Guid id,ReasonRequest r,Writer w,BarOperations op,BarDb db,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"tab.cancel",new{id,r},async()=>{
 var tab=await op.ActiveTab(id);Rules.Text(r.Reason,"Motivo",1000);var items=await db.Items.Where(x=>x.TabId==id&&!x.Cancelled).ToListAsync();foreach(var item in items)await op.CancelItem(item.Id,r,Rules.UserId(u));tab.Status="Cancelled";tab.ClosedAt=DateTimeOffset.UtcNow;return tab;
}))).RequireAuthorization("Admin");
api.MapPost("/tabs/{id:guid}/transfer",async(Guid id,TransferRequest r,Writer w,BarOperations op,BarDb db,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"tab.transfer",new{id,r},async()=>{
 var tab=await op.ActiveTab(id);Rules.Check(await db.Tables.AnyAsync(x=>x.Id==r.TableId&&x.Active),"Mesa não encontrada.",404);Rules.Check(!await db.Tabs.AnyAsync(x=>x.TableId==r.TableId&&(x.Status=="Open"||x.Status=="Closing")),"Mesa de destino ocupada.",409);tab.TableId=r.TableId;return tab;
}))).RequireAuthorization("Admin");
api.MapPost("/tabs/{id:guid}/merge",async(Guid id,MergeRequest r,Writer w,BarOperations op,BarDb db,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"tab.merge",new{id,r},async()=>{
 Rules.Check(id!=r.SourceTabId,"Comandas devem ser diferentes.");var target=await op.ActiveTab(id);var source=await op.ActiveTab(r.SourceTabId);Rules.Check(target.Status=="Open"&&source.Status=="Open","Junte somente comandas abertas.");
 var items=await db.Items.Where(x=>x.TabId==source.Id).ToListAsync();foreach(var i in items)i.TabId=target.Id;source.Status="Merged";source.ClosedAt=DateTimeOffset.UtcNow;source.Notes+=$" Unida à comanda {target.Number}.";return target;
}))).RequireAuthorization("Admin");
api.MapGet("/receivables",async(BarDb db)=>await db.Receivables.Join(db.Customers,r=>r.CustomerId,c=>c.Id,(r,c)=>new{r.Id,r.CustomerId,customerName=c.Name,r.SaleId,r.Original,r.Balance,r.DueAt}).OrderBy(x=>x.DueAt).ToListAsync()).RequireAuthorization("Receive");
api.MapPost("/receivables/{id:guid}/receive",async(Guid id,ReceiveRequest r,Writer w,BarOperations op,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"credit.receive",new{id,r},()=>op.Receive(id,r,Rules.UserId(u))))).RequireAuthorization("Receive");
api.MapGet("/payments",async(BarDb db)=>await db.Payments.OrderByDescending(x=>x.CreatedAt).Take(500).ToListAsync()).RequireAuthorization("Receive");
api.MapPost("/payments/{id:guid}/reverse",async(Guid id,ReasonRequest r,Writer w,BarOperations op,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"payment.reverse",new{id,r},()=>op.ReversePayment(id,r,Rules.UserId(u))))).RequireAuthorization("Admin");
api.MapGet("/cash",async(BarDb db,BarOperations op)=>{var current=await db.CashSessions.SingleOrDefaultAsync(x=>x.IsOpen);return Results.Ok(new{current,expected=current is null?0:await op.CashExpected(current),movements=await db.CashMovements.OrderByDescending(x=>x.CreatedAt).Take(200).ToListAsync(),sessions=await db.CashSessions.OrderByDescending(x=>x.OpenedAt).Take(50).ToListAsync()});}).RequireAuthorization("CashAccess");
api.MapPost("/cash/open",async(CashOpenRequest r,Writer w,BarDb db,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"cash.open",r,async()=>{Rules.Money(r.Initial);Rules.Check(!await db.CashSessions.AnyAsync(x=>x.IsOpen),"Já existe caixa aberto.",409);var cash=new CashSession{Initial=r.Initial,OpenedBy=Rules.UserId(u)};db.CashSessions.Add(cash);return cash;}))).RequireAuthorization("Admin");
api.MapPost("/cash/movements",async(CashEntryRequest r,Writer w,BarDb db,BarOperations op,ClaimsPrincipal u)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"cash.movement",r,async()=>{
 Rules.Check(r.Kind is "Expense" or "Withdrawal" or "Supply","Tipo de movimentação inválido.");Rules.Money(r.Amount,true);Rules.Method(r.Method);var cash=await op.OpenCash();var m=new CashMovement{CashSessionId=cash.Id,UserId=Rules.UserId(u),Kind=r.Kind,Amount=r.Kind=="Supply"?r.Amount:-r.Amount,Method=r.Method,Description=Rules.Text(r.Description,"Descrição",1000)};db.CashMovements.Add(m);return m;
}))).RequireAuthorization("Admin");
api.MapPost("/cash/close",async(CashCloseRequest r,Writer w,BarDb db,BarOperations op,ClaimsPrincipal u)=>{Rules.CloseCash(u);return Json(await w.Run(r.OperationId,Rules.UserId(u),"cash.close",r,async()=>{Rules.Money(r.Counted);var cash=await op.OpenCash();cash.Expected=await op.CashExpected(cash);cash.Counted=r.Counted;cash.ClosedAt=DateTimeOffset.UtcNow;cash.ClosedBy=Rules.UserId(u);cash.IsOpen=false;return cash;}));});
api.MapGet("/users",async(BarDb db)=>await db.Users.Select(x=>new{x.Id,x.Name,x.Login,x.Role,x.CanReceive,x.CanCloseCash,x.Active}).ToListAsync()).RequireAuthorization("Admin");
api.MapPost("/users",async(UserRequest r,Writer w,BarDb db,ClaimsPrincipal u,IPasswordHasher<User> hasher)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"user.create",r with{Password=Rules.Hash(r.Password??"")},()=>{var user=new User();UpdateUser(user,r,hasher);db.Users.Add(user);return Task.FromResult<object>(new{user.Id,user.Name,user.Role});}))).RequireAuthorization("Admin");
api.MapPut("/users/{id:guid}",async(Guid id,UserRequest r,Writer w,BarDb db,ClaimsPrincipal u,IPasswordHasher<User> hasher)=>Json(await w.Run(r.OperationId,Rules.UserId(u),"user.update",new{id,r=r with{Password=Rules.Hash(r.Password??"")}},async()=>{var user=await db.Users.FindAsync(id)??throw new RuleException("Usuário não encontrado.",404);Rules.Check(id!=Rules.UserId(u)||r.Active&&r.Role=="Admin","Você não pode remover seu próprio acesso administrativo.");UpdateUser(user,r,hasher);return new{user.Id,user.Name,user.Role};}))).RequireAuthorization("Admin");
api.MapGet("/audit",async(BarDb db)=>await db.Audits.OrderByDescending(x=>x.CreatedAt).Take(500).ToListAsync()).RequireAuthorization("Admin");
api.MapGet("/sync/status",async(BarDb db,IConfiguration c)=>{var health=await db.SyncHealth.FindAsync(1);var pending=await db.Outbox.CountAsync(x=>x.SentAt==null);var configured=c["Node:Mode"]=="Replica"||!string.IsNullOrEmpty(c["Sync:RemoteUrl"]);return Results.Ok(new{mode=c["Node:Mode"]??"Authority",configured,pending,lastSuccess=health?.LastSuccess,error=health?.Error??"",connected=configured&&health?.LastSuccess>DateTimeOffset.UtcNow.AddMinutes(-1)&&string.IsNullOrEmpty(health.Error),backup=await db.BackupRuns.OrderByDescending(x=>x.At).FirstOrDefaultAsync(),warning=pending>0?"Operações pendentes ainda não estão protegidas no servidor remoto. A perda deste computador pode causar perda desses dados.":""});});
app.MapPost("/api/sync/ingest",async(Envelope e,Replicator replica,HttpContext ctx,IConfiguration c)=>{ServiceKey(ctx,c,"Sync:Key","X-Sync-Key");await replica.Apply(e);return Results.Ok(new{e.Sequence,e.Id});});
app.MapPost("/api/backup/status",async(BackupRequest r,BarDb db,HttpContext ctx,IConfiguration c)=>{ServiceKey(ctx,c,"Backup:StatusKey","X-Backup-Key");db.BackupRuns.Add(new BackupRun{Success=r.Success,Message=Rules.Text(r.Message,"Mensagem",500,false)});await db.SaveChangesAsync();return Results.Ok();});
api.MapGet("/dashboard",Reports.Dashboard).RequireAuthorization("Admin");
api.MapGet("/reports",Reports.Report).RequireAuthorization("Admin");
api.MapGet("/reports.csv",Reports.Csv).RequireAuthorization("Admin");
app.MapOpenApi().RequireAuthorization("Admin");
using(var scope=app.Services.CreateScope()) {
 var db=scope.ServiceProvider.GetRequiredService<BarDb>();
 if(builder.Configuration.GetValue("Database:Migrate",false)) await db.Database.MigrateAsync();
 if(builder.Configuration["Node:Mode"]!="Replica"&&!await db.Users.AnyAsync()) {
  var password=builder.Configuration["Bootstrap:AdminPassword"]??"";
  Rules.Check(password.Length>=12&&password.Length<=128,"Configure Bootstrap__AdminPassword com 12 a 128 caracteres antes da primeira inicialização.");
  var user=new User{Name="Administrador",Login="admin",Role="Admin",CanReceive=true,CanCloseCash=true};user.PasswordHash=scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(user,password);
  await scope.ServiceProvider.GetRequiredService<Writer>().Run(Guid.NewGuid(),user.Id,"bootstrap",new{user.Login},()=>{db.Users.Add(user);return Task.FromResult<object>(new{user.Id,user.Login});});
 }
}
app.Run();
static void ServiceKey(HttpContext ctx,IConfiguration c,string name,string header) {var expected=c[name]??"";var actual=ctx.Request.Headers[header].ToString();Rules.Check(expected.Length>=32&&CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected),Encoding.UTF8.GetBytes(actual)),"Acesso de serviço negado.",401);}
static void UpdateProduct(Product p,ProductRequest r) {p.Code=Rules.Text(r.Code,"Código",80);p.Name=Rules.Text(r.Name,"Nome");p.Category=Rules.Text(r.Category,"Categoria",100,false);p.Unit=Rules.Text(r.Unit,"Unidade",20);p.Barcode=Rules.Text(r.Barcode,"Código de barras",80,false);p.Supplier=Rules.Text(r.Supplier,"Fornecedor",200,false);p.Cost=Rules.Money(r.Cost);p.Price=Rules.Money(r.Price);p.MinimumStock=Rules.Quantity(r.MinimumStock,false);Rules.Check(p.MinimumStock>=0,"Estoque mínimo inválido.");p.Active=r.Active;}
static void UpdateCustomer(Customer c,CustomerRequest r){c.Name=Rules.Text(r.Name,"Nome");c.Nickname=Rules.Text(r.Nickname,"Apelido",100,false);c.Phone=Rules.Text(r.Phone,"Telefone",30,false);c.WhatsApp=Rules.Text(r.WhatsApp,"WhatsApp",30,false);c.Address=Rules.Text(r.Address,"Endereço",300,false);c.Notes=Rules.Text(r.Notes,"Observações",1000,false);c.CreditLimit=Rules.Money(r.CreditLimit);c.CreditAuthorized=r.CreditAuthorized;c.Active=r.Active;}
static void UpdateUser(User u,UserRequest r,IPasswordHasher<User> h){u.Name=Rules.Text(r.Name,"Nome");u.Login=Rules.Text(r.Login,"Login",80).ToLowerInvariant();Rules.Check(r.Role is "Admin" or "Employee","Perfil inválido.");if(!string.IsNullOrEmpty(r.Password)){Rules.Check(r.Password.Length is >=12 and <=128,"Senha deve ter de 12 a 128 caracteres.");u.PasswordHash=h.HashPassword(u,r.Password);}Rules.Check(u.PasswordHash.Length>0,"Senha obrigatória.");u.Role=r.Role;u.Active=r.Active;u.CanReceive=r.CanReceive;u.CanCloseCash=r.CanCloseCash;}
public partial class Program { }
