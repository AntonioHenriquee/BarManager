import { Injectable } from '@angular/core';
@Injectable({providedIn:'root'})
export class Api {
 async request(path:string,method='GET',body?:unknown):Promise<any> {
  let response:Response;
  try { response=await fetch('/api'+path,{method,credentials:'same-origin',headers:{'Content-Type':'application/json','X-MeuBar':'1'},body:body===undefined?undefined:JSON.stringify(body)}); }
  catch {throw new Error('Sem comunicação com o servidor local. Confira a conexão antes de repetir a operação.');}
  if(!response.ok){let data:any={};try{data=await response.json();}catch{} throw new Error(data.error||(response.status===403?'Seu perfil não tem autorização para esta operação.':response.status===401?'Sessão expirada ou acesso negado. Entre novamente.':`Não foi possível concluir (HTTP ${response.status}).`));}
  return response.status===204?null:response.json();
 }
 get(path:string){return this.request(path);}
 post(path:string,body:unknown){return this.request(path,'POST',body);}
}
