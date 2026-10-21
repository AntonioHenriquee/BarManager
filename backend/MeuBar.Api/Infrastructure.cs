using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeuBar;
public class RuleException(string message, int status=400) : Exception(message) { public int Status {get;}=status; }
public static class Rules {
 public static void Check(bool valid,string message,int status=400) { if(!valid) throw new RuleException(message,status); }
 public static string Text(string? value,string name,int max=200,bool required=true) { var v=(value??"").Trim(); Check((!required||v.Length>0)&&v.Length<=max,$"{name} inválido (máximo {max} caracteres)."); return v; }
 public static decimal Money(decimal value,bool positive=false) { Check(value>=0&&(!positive||value>0)&&value<=999999999&&decimal.Round(value,2)==value,"Valor monetário inválido; use até duas casas decimais."); return value; }
 public static decimal Quantity(decimal value,bool positive=true) { Check((!positive||value>0)&&Math.Abs(value)<=999999999&&decimal.Round(value,3)==value,"Quantidade inválida; use até três casas decimais."); return value; }
 public static readonly string[] Methods=["Cash","Pix","Debit","Credit","Other"];
 public static string Method(string value) { Check(Methods.Contains(value),"Forma de pagamento inválida."); return value; }
 public static Guid UserId(ClaimsPrincipal user)=>Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
 public static void Admin(ClaimsPrincipal user)=>Check(user.IsInRole("Admin"),"Operação exclusiva do administrador.",403);
 public static void Receive(ClaimsPrincipal user)=>Check(user.IsInRole("Admin")||user.HasClaim("receive","true"),"Sem autorização para recebimentos.",403);
 public static void CloseCash(ClaimsPrincipal user)=>Check(user.IsInRole("Admin")||user.HasClaim("closeCash","true"),"Sem autorização para fechar caixa.",403);
 public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
public class BarAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder,BarDb db)
 : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder) {
 protected override async Task<AuthenticateResult> HandleAuthenticateAsync() {
  var token=Request.Cookies["meubar_session"]; if(string.IsNullOrEmpty(token)) return AuthenticateResult.NoResult();
  var hash=Rules.Hash(token); var session=await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x=>x.TokenHash==hash&&x.ExpiresAt>DateTimeOffset.UtcNow);
  if(session is null) return AuthenticateResult.Fail("Sessão expirada.");
  var user=await db.Users.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==session.UserId&&x.Active);
  if(user is null) return AuthenticateResult.Fail("Usuário inativo.");
  Claim[] claims=[new(ClaimTypes.NameIdentifier,user.Id.ToString()),new(ClaimTypes.Name,user.Name),new(ClaimTypes.Role,user.Role),new("receive",user.CanReceive?"true":"false"),new("closeCash",user.CanCloseCash?"true":"false")];
  return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name));
 }
}
public record Change(string Type,long PreviousVersion,JsonElement Data);
public record Envelope(long Sequence,Guid Id,string DeviceId,string Payload);
public class Writer(BarDb db,IConfiguration config) {
 public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 public async Task<string> Run(Guid operationId,Guid userId,string kind,object request,Func<Task<object>> action) {
  Rules.Check(operationId!=Guid.Empty,"Identificador da operação obrigatório.");
  Rules.Check(config["Node:Mode"]!="Replica","Réplica somente para consulta. Escreva no servidor de autoridade.",409);
  await using var tx=await db.Database.BeginTransactionAsync();
  // Serializa comandos financeiros de um estabelecimento, inclusive processos diferentes.
  await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(784120001)");
  var hash=Rules.Hash(JsonSerializer.Serialize(request,Json));
  var existing=await db.Operations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==operationId);
  if(existing is not null) { Rules.Check(existing.UserId==userId&&existing.Kind==kind&&existing.RequestHash==hash,"Identificador já utilizado por outra operação.",409); return existing.Result; }
  var result=await action();
  db.Audits.Add(new Audit{UserId=userId,Action=kind,Details=operationId.ToString()});
  db.ChangeTracker.DetectChanges();
  var changed=db.ChangeTracker.Entries<Record>().Where(e=>e.State is EntityState.Added or EntityState.Modified).ToList();
  var versions=changed.ToDictionary(e=>e.Entity.Id,e=>e.State==EntityState.Added?0:e.Entity.Version);
  foreach(var entry in changed) entry.Entity.Version=versions[entry.Entity.Id]+1;
  await db.SaveChangesAsync();
  var json=JsonSerializer.Serialize(result,Json);
  var operation=new Operation{Id=operationId,UserId=userId,Kind=kind,RequestHash=hash,Result=json,Version=1};
  db.Operations.Add(operation);
  var changes=changed.Select(e=>new Change(e.Entity.GetType().Name,versions[e.Entity.Id],JsonSerializer.SerializeToElement(e.Entity,e.Entity.GetType(),Json))).ToList();
  changes.Add(new Change(nameof(Operation),0,JsonSerializer.SerializeToElement(operation,Json)));
  db.Outbox.Add(new Outbox{Sequence=(await db.Outbox.MaxAsync(x=>(long?)x.Sequence)??0)+1,DeviceId=config["Node:DeviceId"]??"local",Payload=JsonSerializer.Serialize(changes,Json)});
  await db.SaveChangesAsync(); await tx.CommitAsync(); return json;
 }
}
public class Replicator(BarDb db,IConfiguration config) {
 public static readonly Dictionary<string,Type> Types=new[]{typeof(User),typeof(Customer),typeof(Product),typeof(ServiceTable),typeof(Tab),typeof(TabItem),typeof(Sale),typeof(Receivable),typeof(Payment),typeof(StockMovement),typeof(CashSession),typeof(CashMovement),typeof(Audit),typeof(Operation)}.ToDictionary(x=>x.Name);
 public async Task Apply(Envelope envelope) {
  Rules.Check(config["Node:Mode"]=="Replica","Recebimento permitido somente em réplica.",409);
  Rules.Check(envelope.Sequence>0&&envelope.Id!=Guid.Empty&&!string.IsNullOrEmpty(envelope.Payload),"Envelope inválido.");
  Rules.Check(envelope.DeviceId==config["Sync:AuthorityDeviceId"],"Dispositivo sem autoridade.",409);
  await using var tx=await db.Database.BeginTransactionAsync();
  await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(784120001)");
  var state=await db.ReplicaStates.SingleOrDefaultAsync(x=>x.DeviceId==envelope.DeviceId);
  var prior=await db.Outbox.AsNoTracking().SingleOrDefaultAsync(x=>x.Sequence==envelope.Sequence);
  if(prior is not null) { Rules.Check(prior.Id==envelope.Id&&prior.Payload==envelope.Payload,"Conflito de conteúdo em operação já sincronizada.",409); return; }
  Rules.Check(envelope.Sequence==(state?.Sequence??0)+1,"Lacuna na sequência; sincronização interrompida.",409);
  var changes=JsonSerializer.Deserialize<List<Change>>(envelope.Payload,Writer.Json)??throw new RuleException("Envelope inválido.");
  Rules.Check(changes.Count is >0 and <=10000,"Quantidade de alterações inválida.");
  foreach(var change in changes) {
   Rules.Check(Types.ContainsKey(change.Type),"Tipo de alteração desconhecido.");
   var type=Types[change.Type]; var entity=(Record?)change.Data.Deserialize(type,Writer.Json)??throw new RuleException("Registro inválido.");
   var current=(Record?)await db.FindAsync(type,entity.Id);
   Rules.Check((current?.Version??0)==change.PreviousVersion&&entity.Version==change.PreviousVersion+1,"Conflito de versão; intervenção administrativa necessária.",409);
   if(current is null) db.Add(entity); else db.Entry(current).CurrentValues.SetValues(entity);
  }
  if(state is null) { state=new ReplicaState{DeviceId=envelope.DeviceId}; db.ReplicaStates.Add(state); }
  state.Sequence=envelope.Sequence; state.AppliedAt=DateTimeOffset.UtcNow;
  var health=await db.SyncHealth.FindAsync(1);if(health is null){health=new();db.SyncHealth.Add(health);}health.LastSuccess=state.AppliedAt;health.Error="";
  // Mantém journal para deduplicação e recuperação. OVERRIDING SYSTEM VALUE preserva números da autoridade.
  await db.SaveChangesAsync();
  await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Outbox\" (\"Sequence\",\"Id\",\"DeviceId\",\"Payload\",\"CreatedAt\",\"SentAt\") VALUES ({envelope.Sequence},{envelope.Id},{envelope.DeviceId},{envelope.Payload},{DateTimeOffset.UtcNow},{DateTimeOffset.UtcNow})");
  await tx.CommitAsync();
 }
}
public class SyncWorker(IServiceScopeFactory scopes,IHttpClientFactory clients,IConfiguration config,ILogger<SyncWorker> logger) : BackgroundService {
 protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
  while(!stoppingToken.IsCancellationRequested) {
   if(config["Node:Mode"]!="Replica"&&!string.IsNullOrWhiteSpace(config["Sync:RemoteUrl"])) {
    using var scope=scopes.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<BarDb>();
    try {
     using var client=clients.CreateClient(); client.Timeout=TimeSpan.FromSeconds(20);
     client.DefaultRequestHeaders.Add("X-Sync-Key",config["Sync:Key"]);
     var pending=await db.Outbox.Where(x=>x.SentAt==null).OrderBy(x=>x.Sequence).Take(50).ToListAsync(stoppingToken);
     foreach(var op in pending) {
      var response=await client.PostAsJsonAsync(config["Sync:RemoteUrl"]!.TrimEnd('/')+"/api/sync/ingest",new Envelope(op.Sequence,op.Id,op.DeviceId,op.Payload),stoppingToken);
      if(!response.IsSuccessStatusCode) throw new InvalidOperationException($"Réplica retornou HTTP {(int)response.StatusCode}; operação {op.Sequence} retida.");
      op.SentAt=DateTimeOffset.UtcNow;
      var health=await db.SyncHealth.FindAsync([1],stoppingToken); if(health is null) {health=new(); db.SyncHealth.Add(health);} health.LastSuccess=DateTimeOffset.UtcNow; health.Error="";
      await db.SaveChangesAsync(stoppingToken);
     }
     if(pending.Count==0) {
      var response=await client.GetAsync(config["Sync:RemoteUrl"]!.TrimEnd('/')+"/health",stoppingToken); response.EnsureSuccessStatusCode();
      var health=await db.SyncHealth.FindAsync([1],stoppingToken); if(health is null){health=new();db.SyncHealth.Add(health);} health.LastSuccess=DateTimeOffset.UtcNow;health.Error="";await db.SaveChangesAsync(stoppingToken);
     }
    } catch(Exception ex) when(!stoppingToken.IsCancellationRequested) {
     logger.LogWarning("Sincronização indisponível: {Type}",ex.GetType().Name);
     db.ChangeTracker.Clear(); var health=await db.SyncHealth.FindAsync([1],stoppingToken); if(health is null){health=new();db.SyncHealth.Add(health);} health.Error=ex is InvalidOperationException?ex.Message:"Sem comunicação com a réplica remota."; await db.SaveChangesAsync(stoppingToken);
    }
   }
   await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Sync:IntervalSeconds",15),1,300)),stoppingToken);
  }
 }
}
